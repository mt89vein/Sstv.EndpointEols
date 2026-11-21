# Sample application

The sample in [`tests/Sstv.EndpointEol.Sample`](../tests/Sstv.EndpointEol.Sample) is a runnable
ASP.NET Core application that demonstrates every lifecycle phase, the diagnostics endpoint and the
Swagger annotations.

## Run it

```bash
dotnet run --project tests/Sstv.EndpointEol.Sample
```

Then open <http://localhost:5080/swagger>. The root URL redirects to the Swagger UI.

The sample ships with `EndOfLife:EnforcementMode=Enforce`, so the blocked endpoints immediately
return `410 Gone` and the warning-phase endpoint shows the `Deprecation` header.

## Endpoints

| Endpoint | Lifecycle | What you see |
|---|---|---|
| `GET /api/v1/books` | Not marked | Normal response, no end-of-life headers. |
| `GET /api/v1/books/{id:guid}` | Not marked | Normal response. |
| `DELETE /api/v1/books/{id:guid}` | Warning (`2026-01-01` → `2027-01-01`) | `204`, `Sunset`, `Deprecation`, `Link` headers; operation marked deprecated in Swagger, with a link to the v2 replacement. |
| `DELETE /api/v2/books/{id:guid}` | Not marked | Replacement target of the v1 action; the deep link in the v1 description jumps here. |
| `POST /api/v1/reports/export` | Active (`2098-01-01` → `2099-01-01`) | `202`, only `Sunset` and documentation links; Swagger links to the v2 export. |
| `POST /api/v2/reports/export` | Not marked | Replacement target of the v1 export. |
| `GET /api/v1/legacy/status` | Blocked (controller-level, `2025-01-01` → `2026-01-01`) | `410 Gone` with ProblemDetails; Swagger links to the external migration guide. |
| `GET /api/v1/legacy/ping` | Blocked (Minimal API, `2025-01-01` → `2026-01-01`) | `410 Gone`; Swagger links to the v2 ping. |
| `GET /api/v2/ping` | Not marked | Replacement target of the deprecated Minimal API endpoint. |
| `GET /api/v1/diagnostics/end-of-life` | Diagnostics | JSON report of all four marked endpoints. |
| `GET /swagger/v1/swagger.json` | OpenAPI | Deprecated operations, `x-sunset` extensions, replacement deep links, `Deprecated / EOL` tag. |

The dates are fixed constants so the sample keeps demonstrating all three phases over time. At the
time of writing the book deletion is in its warning phase, the legacy endpoints are blocked and the
report export is active.

In Swagger UI, expand `DELETE /api/v1/books/{id}` and click the replacement in the description: the
UI jumps to `DELETE /api/v2/books/{id}`. The same works for the reports export and the Minimal API
ping; the legacy controller points to an external URL instead.

## Try it with curl

```bash
# Warning phase: 204 with Sunset, Deprecation and Link headers.
curl -i -X DELETE \
  http://localhost:5080/api/v1/books/11111111-1111-1111-1111-111111111111

# Blocked: 410 Gone with application/problem+json.
curl -i http://localhost:5080/api/v1/legacy/status

# Minimal API endpoint: also blocked.
curl -i http://localhost:5080/api/v1/legacy/ping

# Active phase: 202 with only the Sunset announcement.
curl -i -X POST http://localhost:5080/api/v1/reports/export

# Full retirement schedule.
curl http://localhost:5080/api/v1/diagnostics/end-of-life
```

## Switch enforcement mode

Edit [`tests/Sstv.EndpointEol.Sample/appsettings.json`](../tests/Sstv.EndpointEol.Sample/appsettings.json):

```json
{
  "EndOfLife": {
    "EnforcementMode": "WarnOnly"
  }
}
```

Restart the sample. The legacy endpoints now return their normal responses with warning headers and
logs instead of `410`.

The mode can also be overridden with an environment variable:

```bash
# bash
EndOfLife__EnforcementMode=WarnOnly dotnet run --project tests/Sstv.EndpointEol.Sample

# PowerShell
$env:EndOfLife__EnforcementMode = "WarnOnly"
dotnet run --project tests/Sstv.EndpointEol.Sample
```

Valid values: `Off`, `MetricsOnly`, `WarnOnly`, `Enforce`.

## Integration tests

The sample is also the system under test for
[`tests/Sstv.EndpointEol.IntegrationTests`](../tests/Sstv.EndpointEol.IntegrationTests). The test
factory boots it with `WebApplicationFactory<Program>`, replaces `TimeProvider` with a
`FakeTimeProvider` and optionally overrides `EnforcementMode`. That makes all three phases
deterministic:

```bash
dotnet test tests/Sstv.EndpointEol.IntegrationTests
```

Covered scenarios:

- no headers on unmarked endpoints;
- `Sunset` only in the active phase;
- `Deprecation` in the warning phase;
- `410 Gone` with ProblemDetails, `Cache-Control`, `Location` and replacement in the blocked phase;
- `WarnOnly` does not block;
- controller-level attributes and Minimal API metadata;
- diagnostics report ordering, phases and days to block;
- Swagger `deprecated` flag, vendor extensions, replacement deep links and the `Deprecated / EOL` tag;
- the `eol.requests` metric with route, method and phase tags.
