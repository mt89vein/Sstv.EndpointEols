# Getting started

## Requirements

- .NET 9
- An ASP.NET Core application (MVC controllers, Minimal APIs, or both)

## Install

```bash
dotnet add package Sstv.EndpointEol
dotnet add package Sstv.EndpointEol.Swashbuckle   # optional, only with Swashbuckle
```

## Register services

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndOfLife(builder.Configuration.GetSection("EndOfLife"));

// Optional: OpenAPI annotations.
builder.Services.AddEndOfLifeSwagger();
builder.Services.AddSwaggerGen();

var app = builder.Build();
```

`AddEndOfLife` registers the middleware dependencies, the diagnostics registry, OpenTelemetry
instruments, options validation and the startup validation hosted service.

The configuration section is optional; options can also be configured with a delegate:

```csharp
builder.Services.AddEndOfLife(configure: options =>
{
    options.EnforcementMode = EnforcementMode.WarnOnly;
    options.SunsetDocsUrl = new Uri("https://docs.example.com/api-lifecycle");
});
```

## Add the middleware

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseEndOfLife();

app.MapControllers();
```

The middleware must run after `UseRouting`, because it reads the endpoint selected by routing.
Placing it after authorization makes the authenticated consumer identity available in logs and
metrics. The library does not authenticate or authorize anything itself.

## Map the diagnostics endpoint

```csharp
app.MapEndOfLifeDiagnostics("/api/v1/diagnostics/end-of-life")
   .RequireAuthorization();
```

The default pattern is `_meta/end-of-life`. The endpoint returns a JSON report of every marked
endpoint, sorted by block date. See [Diagnostics API](diagnostics.md).

## Mark an endpoint

### MVC action

```csharp
[HttpDelete("{id:guid}")]
[EndOfLife("2027-01-01", WarnAfter = "2026-10-23", ReplacedBy = "/api/v2/books/{id}")]
public IActionResult Delete(Guid id) => NoContent();
```

### Whole controller

```csharp
[EndOfLife("2026-01-01", WarnAfter = "2025-01-01", ReplacedBy = "https://example.com/migrate")]
public sealed class LegacyController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok();
}
```

An action-level attribute overrides a controller-level one. This is useful when one action gets an
extension while the rest of the controller is retired on schedule.

### Minimal API

```csharp
app.MapGet("/api/v1/legacy/ping", () => Results.Ok(new { pong = true }))
   .WithEndOfLife("2027-01-01", warnAfter: "2026-10-23", replacedBy: "/api/v2/ping");
```

`WithEndOfLife` attaches the same metadata as the attribute, so headers, metrics, blocking,
diagnostics and Swagger work identically.

## Dates

Attribute arguments must be compile-time constants, so dates are ISO 8601 strings in
`yyyy-MM-dd` format:

- `BlockAfter` (required) — the date from which the endpoint is blocked.
- `WarnAfter` (optional) — the date from which warnings and the `Deprecation` header begin.
- `ReplacedBy` (optional) — a successor path (starting with `/`) or a free-form migration hint.

`WarnAfter` defaults to **90 days before** `BlockAfter`. Without a warning window an endpoint would
jump straight from active to blocked, leaving no period in which remaining consumers could be
observed — which is the main value of the feature.

Dates are date-only. Boundaries are inclusive at `00:00 UTC`: on the `BlockAfter` date the endpoint
is already blocked.

## Fail-fast validation

All marked endpoints are validated when the application starts. An unparsable date or a
`WarnAfter` that is not earlier than `BlockAfter` fails startup with a descriptive exception.
The schedule is also written to the log once, so forgotten dates are visible in CI or development
logs without calling the diagnostics endpoint.

## Next steps

- [Configuration](configuration.md) — enforcement modes and options.
- [HTTP contract](http-contract.md) — what clients actually see.
- [Rollout guide](rollout.md) — how to retire an endpoint safely.
