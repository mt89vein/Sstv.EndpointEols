# Configuration

## Options

| Option | Default | Description |
|---|---|---|
| `EnforcementMode` | `WarnOnly` | Global kill switch. See [Enforcement modes](#enforcement-modes). Read per request, so configuration reloads apply without a restart. |
| `MeterName` | `Sstv.EndpointEol` | OpenTelemetry meter name. It must be subscribed with `AddMeter` to be exported. |
| `LogThrottleMinutes` | `1` | Minimum interval between warning logs for the same route + phase + consumer. `0` disables throttling. |
| `SunsetDocsUrl` | — | Absolute URL of a page describing the sunset process. Announced with `Link rel="sunset"`. |
| `UserIdClaimType` | `sub` | Claim type used as the consumer identity in logs and consumer metrics. Falls back to `ClaimTypes.NameIdentifier`, which covers JWT handlers with `MapInboundClaims` enabled. |
| `CollectConsumers` | `true` | Tracks distinct consumers per route and exposes `eol.consumers{route}`. In-memory state grows with the number of distinct consumers. |
| `GoneProblemDetailsFactory` | — | Builds the `410 Gone` body. Use it to match an application-specific error contract. |

## Binding

From configuration:

```csharp
builder.Services.AddEndOfLife(builder.Configuration.GetSection("EndOfLife"));
```

```json
{
  "EndOfLife": {
    "EnforcementMode": "WarnOnly",
    "LogThrottleMinutes": 1,
    "SunsetDocsUrl": "https://docs.example.com/api-lifecycle",
    "UserIdClaimType": "sub",
    "CollectConsumers": true
  }
}
```

From a delegate:

```csharp
builder.Services.AddEndOfLife(configure: options =>
{
    options.EnforcementMode = EnforcementMode.WarnOnly;
    options.SunsetDocsUrl = new Uri("https://docs.example.com/api-lifecycle");
});
```

Both can be combined. Configuration is bound first, then the delegate is applied.

## Enforcement modes

| Mode | Headers | Warning logs | Metrics | `410` after `BlockAfter` |
|---|---|---|---|---|
| `Off` | — | — | — | — |
| `MetricsOnly` | — | — | ✅ | — |
| `WarnOnly` (default) | ✅ | ✅ | ✅ | — |
| `Enforce` | ✅ | ✅ | ✅ | ✅ |

- **`Off`** disables the feature completely, including metrics. Useful for load tests or local
  development where headers would only add noise.
- **`MetricsOnly`** is silent observation: `eol.requests` and `eol.consumers` are emitted, but no
  header is added, nothing is logged and nothing is blocked. Use it to roll the feature out where
  clients must not observe any change in the response.
- **`WarnOnly`** is the recommended starting point: clients see the sunset headers, the server logs
  remaining consumers and the metrics answer "who is still calling this?".
- **`Enforce`** additionally rejects requests past `BlockAfter` with `410 Gone`.

`EnforcementMode` is read through `IOptionsMonitor` on every request, so switching it through
configuration takes effect immediately — no restart, no redeploy.

## Custom `410` payload

The default body is `application/problem+json` with `type`, `title`, `status`, `detail`, `sunset`,
`replacement` and `traceId`. Replace it when your API has its own error envelope:

```csharp
builder.Services.AddEndOfLife(configure: options =>
{
    options.GoneProblemDetailsFactory = (context, policy) => new GoneProblemDetails
    {
        Type = "https://errors.example.com/end-of-life",
        Title = "Endpoint retired",
        Status = StatusCodes.Status410Gone,
        Detail = $"Retired on {policy.BlockAfter:yyyy-MM-dd}. Migrate to {policy.ReplacedBy}.",
        Sunset = policy.BlockAfter.ToString("yyyy-MM-dd"),
        Replacement = policy.ReplacedBy,
        TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
        Extensions = new Dictionary<string, object?>
        {
            ["code"] = "ENDPOINT_EOL"
        }
    };
});
```

`Extensions` entries are merged into the JSON object as-is. The response is always sent with
`Cache-Control: no-store`, regardless of the factory.

## Options validation

Invalid option values fail startup:

- `EnforcementMode` must be a defined enum value.
- `MeterName` must be non-empty.
- `LogThrottleMinutes` must be non-negative.

Endpoint attributes are validated separately by the startup validation service. See
[Getting started](getting-started.md#fail-fast-validation).
