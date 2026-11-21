# Sstv.EndpointEol

Declarative end-of-life (sunsetting) management for ASP.NET Core endpoints.

Announce retirement dates with standard HTTP headers, measure who still uses legacy endpoints,
and block them when the time comes — all driven by a single attribute on a controller action or a
Minimal API endpoint.

```csharp
[EndOfLife("2027-01-01", WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/books/{id}")]
[HttpDelete("{id:guid}")]
public IActionResult Delete(Guid id) => NoContent();
```

The endpoint immediately starts advertising its sunset date, enters an observable warning phase,
and finally returns `410 Gone` with a migration hint. A global kill switch guarantees that nothing
is blocked until you explicitly decide to enforce the schedule.

## Why

Retiring an API endpoint is usually ad-hoc: someone deletes the action, and clients start receiving
meaningless `404 Not Found`. There is no standard way to answer the three questions that matter
during a shutdown:

1. **When** will this endpoint stop working?
2. **Who** is still using it, and how much?
3. **What** should consumers migrate to?

HTTP already has the vocabulary for this (`Sunset` — RFC 8594, `Deprecation` — RFC 9745), but wiring
dates, headers, logging, metrics, blocking and documentation by hand is repetitive boilerplate that
drifts out of sync. `Sstv.EndpointEol` makes the lifecycle declarative and observable.

## Features

- One `[EndOfLife]` attribute on a controller, an action, or a Minimal API endpoint
  (action-level overrides controller-level).
- Standard headers: `Sunset`, `Deprecation`, `Link rel="sunset"`, `Link rel="successor-version"`.
- `410 Gone` + `application/problem+json` after the block date, with `Cache-Control: no-store`,
  a `Location` hint and a payload you can replace with your own error contract.
- OpenTelemetry metrics: `eol.requests{route, method, phase}` counted in every phase and
  `eol.consumers{route}` counting distinct consumers.
- Structured warning logs throttled per route + phase + consumer, so every remaining consumer
  surfaces instead of just the first one.
- Diagnostics JSON endpoint listing all sunsetted APIs, sorted by block date.
- Fail-fast startup validation of every marked endpoint and a one-time startup log of the schedule.
- Four enforcement modes (`Off`, `MetricsOnly`, `WarnOnly`, `Enforce`), re-read per request, so the
  kill switch works without a restart or redeploy.
- Time-based logic goes through `TimeProvider`, making every phase testable with a fake clock.
- Optional Swagger/OpenAPI annotations in a separate package: `deprecated: true`, a human-readable
  sunset note with a clickable deep link to the replacement operation, `x-sunset` vendor extensions
  and a dedicated `Deprecated / EOL` tag.

## Packages

| Package | Purpose |
|---|---|
| `Sstv.EndpointEol` | Runtime: attribute, middleware, metrics, diagnostics, startup validation. |
| `Sstv.EndpointEol.Swashbuckle` | Swashbuckle/OpenAPI annotations. Optional. |

```bash
dotnet add package Sstv.EndpointEol
dotnet add package Sstv.EndpointEol.Swashbuckle   # only if you use Swashbuckle
```

Target framework: **.NET 10** (ASP.NET Core).

## Quick start

**1. Register the services**

```csharp
builder.Services.AddEndOfLife(builder.Configuration.GetSection("EndOfLife"));

// Optional: Swagger annotations from Sstv.EndpointEol.Swashbuckle.
builder.Services.AddEndOfLifeSwagger();
builder.Services.AddSwaggerGen();
```

**2. Add the middleware after routing**

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseEndOfLife();

app.MapControllers();
```

The middleware reads endpoint metadata, so it must run after `UseRouting`. Placing it after
authorization makes the authenticated consumer id available in logs and metrics.

**3. Map the diagnostics endpoint** (optional)

```csharp
app.MapEndOfLifeDiagnostics("/api/v1/diagnostics/end-of-life")
   .RequireAuthorization();
```

**4. Mark endpoints scheduled for removal**

```csharp
// MVC: action-level attribute.
[HttpDelete("{id:guid}")]
[EndOfLife("2027-01-01", WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/books/{id}")]
public IActionResult Delete(Guid id) => NoContent();

// MVC: controller-level attribute, inherited by every action.
[EndOfLife("2026-01-01", WarnAfter = "2025-01-01")]
public sealed class LegacyController : ControllerBase { /* ... */ }

// Minimal API.
app.MapGet("/api/v1/legacy/ping", () => Results.Ok())
   .WithEndOfLife("2027-01-01", warnAfter: "2026-10-23", replacedBy: "/api/v2/ping");
```

Attribute arguments must be compile-time constants, so dates are ISO 8601 strings (`yyyy-MM-dd`).
They are parsed and validated once at startup. `WarnAfter` is optional; when omitted, a 90-day
warning window before `BlockAfter` is assumed.

**5. Configure it**

```json
{
  "EndOfLife": {
    "EnforcementMode": "WarnOnly",
    "LogThrottleMinutes": 1
  }
}
```

`WarnOnly` is the safe default: headers, logs and metrics are emitted, but nothing is blocked.

## Lifecycle phases

| Phase | When | Behaviour |
|---|---|---|
| **Active** | `now < WarnAfter` | Works as usual; announces the sunset date via headers; counted as `phase="active"`. |
| **Warning** | `WarnAfter ≤ now < BlockAfter` | Adds `Deprecation`, logs a warning and counts `phase="warning"`; requests still succeed. |
| **Blocked** | `now ≥ BlockAfter` | Counts `phase="blocked"`; with `Enforce` the request is rejected with `410 Gone`. |

The warning phase is the important one: it shows when real traffic has dropped to zero, so
blocking becomes a formality instead of a leap of faith.

## Sample application

A runnable sample with Swagger lives in [`tests/Sstv.EndpointEol.Sample`](tests/Sstv.EndpointEol.Sample):

```bash
dotnet run --project tests/Sstv.EndpointEol.Sample
```

Open <http://localhost:5080/swagger> to explore the endpoints. The sample ships with
`EnforcementMode=Enforce`, so you can immediately see headers, a warning-phase endpoint and a
`410 Gone` response. See [docs/sample.md](docs/sample.md) for the endpoint matrix and curl examples.

## Documentation

- [Getting started](docs/getting-started.md)
- [Configuration](docs/configuration.md)
- [HTTP contract](docs/http-contract.md)
- [Metrics](docs/metrics.md)
- [Diagnostics API](docs/diagnostics.md)
- [Swagger / OpenAPI](docs/swagger.md)
- [Rollout guide](docs/rollout.md)
- [Sample application](docs/sample.md)
- [Design notes and alternatives](docs/design.md)
- [Changelog](CHANGELOG.md)

## Repository layout

```
src/
  Sstv.EndpointEol/                    Core runtime package
  Sstv.EndpointEol.Swashbuckle/        Optional Swashbuckle integration
tests/
  Sstv.EndpointEol.Sample/             Runnable sample with Swagger UI
  Sstv.EndpointEol.Tests/              Unit tests
  Sstv.EndpointEol.IntegrationTests/   Integration tests over the sample via WebApplicationFactory
docs/                                  User and design documentation
```

## Development

```bash
dotnet build Sstv.EndpointEol.sln
dotnet test  Sstv.EndpointEol.sln

# Produce NuGet packages and symbol packages.
dotnet pack src/Sstv.EndpointEol/Sstv.EndpointEol.csproj -c Release
dotnet pack src/Sstv.EndpointEol.Swashbuckle/Sstv.EndpointEol.Swashbuckle.csproj -c Release
```

The integration tests boot the sample application with `WebApplicationFactory` and a fake clock,
so all three lifecycle phases are covered through a real HTTP pipeline.

## License

MIT — see [LICENSE](LICENSE).
