# Rollout guide

Retiring an endpoint is a process, not a deployment. The library is designed for the following
sequence.

## 1. Announce

Add the attribute with a realistic schedule and ship it with `EnforcementMode=WarnOnly` (the
default):

```csharp
[EndOfLife("2027-01-01", WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/books")]
[HttpDelete("{id:guid}")]
public IActionResult Delete(Guid id) => NoContent();
```

From this release:

- every response carries `Sunset` and documentation links;
- the operation is marked as deprecated in Swagger;
- the endpoint appears in the diagnostics report and in the startup log.

Nothing is blocked.

## 2. Observe

Watch the warning phase:

```promql
sum by (route) (rate(eol_requests_total{phase="warning"}[1h]))
eol_consumers
```

The `eol.consumers` value tells you how many distinct consumers are still active. The warning logs
name them, one entry per consumer per throttle interval.

Notify the remaining consumers while there is still time to migrate.

## 3. Enforce

When warning traffic approaches zero and you have warned the remaining consumers, switch the mode:

```json
{
  "EndOfLife": {
    "EnforcementMode": "Enforce"
  }
}
```

The switch is read per request, so it applies immediately — no restart, no redeploy.

Afterwards, watch:

```promql
sum by (route) (rate(eol_requests_total{phase="blocked"}[1h]))
```

Any remaining traffic receives `410 Gone` with the migration hint. If a consumer was missed, flip
the mode back to `WarnOnly` immediately instead of editing dates and shipping a release.

## 4. Remove

When blocked traffic has been zero for a while, delete the attribute and the dead endpoint in a
regular pull request. The diagnostics report is then empty for that route.

## Emergency controls

| Situation | Action |
|---|---|
| A client did not migrate in time | Set `EnforcementMode=WarnOnly` (or `Off`). |
| The feature interferes with an environment | Set `EnforcementMode=Off`. |
| You need silent measurement first | Set `EnforcementMode=MetricsOnly`. |
| You need to change a date | Ship a release. Dates are part of the API contract; the kill switch exists for emergencies. |

## Limitations

- The middleware must be registered after `UseRouting`; endpoints without routing metadata are
  ignored.
- Works with anything visible in `EndpointDataSource`: MVC controllers, Minimal APIs and custom
  endpoints.
- Dates are date-only, with no time zone or time of day. Boundaries are inclusive at `00:00 UTC`.
- `WarnAfter` defaults to 90 days before `BlockAfter`; set it explicitly for a different window.
- `Location` on a `410` is a non-standard hint, not a redirect. The standard successor announcement
  is `Link rel="successor-version"`.
- Per-consumer state (`eol.consumers`, log throttling) grows with the number of distinct consumers
  per route and lives for the process lifetime.
- Do not mark health checks, Swagger or other infrastructure endpoints.
- Startup validation fails the application on invalid attributes. It runs after the web host starts;
  a failure stops the host before it serves meaningful traffic.
