# Metrics

All instruments are created on the meter configured by `EndOfLifeOptions.MeterName`, which defaults
to `Sstv.EndpointEol`.

Subscribe the meter in your telemetry pipeline:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Sstv.EndpointEol")
        .AddPrometheusExporter());
```

## Instruments

### `eol.requests`

A `Counter<long>` counting requests to endpoints marked with `[EndOfLife]`.

| Tag | Value |
|---|---|
| `route` | Normalized route template, for example `/api/v1/books/{id:guid}`. Never the raw path. |
| `method` | One measurement per HTTP method, lower-cased (`get`, `post`, …). `any` when the method is unknown. |
| `phase` | `active`, `warning` or `blocked` — the phase at the moment of the request. |

Every phase is counted, including `active`. The active phase is the baseline that distinguishes
"traffic dropped to zero" from "nothing was ever measured". Blocked requests are counted too,
including in `WarnOnly` mode, which is exactly when you want to see leftover consumers before
turning enforcement on.

An endpoint that accepts several HTTP methods produces one measurement per method, so `by (method)`
queries stay meaningful.

### `eol.consumers`

An `UpDownCounter<long>` holding the number of distinct consumers observed on the route since the
process started — the direct answer to "who is still calling this?".

| Tag | Value |
|---|---|
| `route` | Normalized route template. |

A consumer is identified by the `UserIdClaimType` claim (default `sub`, with a fallback to
`ClaimTypes.NameIdentifier`). Repeated calls by the same consumer do not increase the value.
Consumers are only tracked outside the active phase. Disable tracking with
`CollectConsumers: false` if you do not want per-consumer in-memory state.

## Useful PromQL queries

```promql
# Remaining warning-phase traffic per route. When it approaches zero, enforcing is safe.
sum by (route) (rate(eol_requests_total{phase="warning"}[1h]))

# Consumers still calling endpoints that are already past their block date.
sum by (route) (rate(eol_requests_total{phase="blocked"}[1h]))

# Share of traffic that is not deprecated yet.
sum by (route) (rate(eol_requests_total{phase="active"}[1h]))
  / sum by (route) (rate(eol_requests_total[1h]))

# Distinct consumers per legacy route.
eol_consumers
```

Metric names in Prometheus depend on the exporter; the examples above assume the default
`_total` suffix for counters and the configured meter prefix.

## Logging and throttling

Warning logs are throttled per `{route, phase, consumer}` for `LogThrottleMinutes` (default 1).
This means every remaining consumer appears in the logs once per interval instead of a single random
one. Metrics still count every request.

Example log record:

```text
Deprecated API called. Route: /api/v1/books/{id:guid}. Phase: Warning.
Days until shutdown: 214. BlockAfter: 2027-01-01. Replacement: /api/v2/books. User: user-42
```

Set `LogThrottleMinutes: 0` to log every request (useful in development).
