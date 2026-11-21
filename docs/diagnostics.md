# Diagnostics API

Map the diagnostics endpoint to expose the whole retirement schedule as JSON:

```csharp
app.MapEndOfLifeDiagnostics("/api/v1/diagnostics/end-of-life")
   .RequireAuthorization();
```

The default pattern is `_meta/end-of-life`. The method returns an
`IEndpointConventionBuilder`, so authorization and other conventions can be applied as usual.

## Response

`GET /api/v1/diagnostics/end-of-life`:

```json
{
  "serverTime": "2026-08-21T09:00:00+00:00",
  "enforcementMode": "warnOnly",
  "items": [
    {
      "method": "DELETE",
      "path": "/api/v1/books/{id:guid}",
      "displayName": "BooksController.Delete (Sample)",
      "warnAfter": "2026-10-23",
      "blockAfter": "2027-01-01",
      "replacedBy": "/api/v2/books",
      "phase": "warning",
      "daysToBlock": 133
    }
  ]
}
```

| Field | Description |
|---|---|
| `serverTime` | UTC time at which the report was generated. |
| `enforcementMode` | Enforcement mode in effect (`off`, `metricsOnly`, `warnOnly`, `enforce`). |
| `items` | All tracked endpoints, sorted by `blockAfter` ascending, then `warnAfter`, then path. |
| `method` | Comma-separated HTTP methods, or `ANY` when the endpoint has no method metadata. |
| `path` | Normalized route template. |
| `displayName` | Endpoint display name, usually the controller action. |
| `warnAfter` | Start of the warning phase (after applying the 90-day default). |
| `blockAfter` | Date from which the endpoint is blocked. |
| `replacedBy` | Replacement hint from the attribute. |
| `phase` | `active`, `warning` or `blocked` at `serverTime`. |
| `daysToBlock` | Days until `blockAfter`; negative when already blocked. |

## Semantics

- The endpoint list is cached and rebuilt when routing endpoints change.
- `phase` and `daysToBlock` are always recalculated against the current time, so a long-lived cache
  never serves stale dates.
- `enforcementMode` is reported next to the schedule, so a report showing `blocked` endpoints in
  `warnOnly` mode is unambiguous: the schedule says blocked, but enforcement is off.
- The response is serialized with the library's own JSON settings (camelCase, enums as camelCase
  strings), so the contract does not depend on how the host application configured its JSON
  options.

## Startup log

The same list is logged once at startup:

```text
End-of-life tracking initialized. 4 endpoint(s) marked with [EndOfLife]. Earliest warning date: 2025-01-01.
EOL endpoint DELETE /api/v1/books/{id:guid}. WarnAfter: 2026-01-01. BlockAfter: 2027-01-01. Phase: Warning. Replacement: /api/v2/books
```

This makes forgotten dates visible in CI or development logs without calling the endpoint. The
startup validation service also fails the application if any marked endpoint has invalid dates.
