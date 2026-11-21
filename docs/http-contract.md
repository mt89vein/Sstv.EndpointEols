# HTTP contract

The library uses standard HTTP headers instead of custom fields, so clients, proxies and code
generators understand the lifecycle without out-of-band documentation.

## Phases

| Phase | Condition | Response |
|---|---|---|
| **Active** | `now < WarnAfter` | Normal response. `Sunset` and documentation links are already present. |
| **Warning** | `WarnAfter ≤ now < BlockAfter` | Normal response plus `Deprecation`. Requests are counted and logged as warnings. |
| **Blocked** | `now ≥ BlockAfter` | Normal response in `WarnOnly`/`MetricsOnly`; `410 Gone` in `Enforce`. |

All dates are date-only and boundaries are inclusive at `00:00 UTC`.

## Headers

| Header | When | Example | Standard |
|---|---|---|---|
| `Sunset` | Always, in every phase | `Sunset: Fri, 01 Jan 2027 00:00:00 GMT` | RFC 8594 |
| `Deprecation` | Warning and blocked phases | `Deprecation: @1798761600` | RFC 9745 |
| `Link` `rel="sunset"` | When `SunsetDocsUrl` is configured | `<https://docs.example.com/api-lifecycle>; rel="sunset"` | RFC 8288 |
| `Link` `rel="successor-version"` | When `ReplacedBy` starts with `/` | `</api/v2/books>; rel="successor-version"` | RFC 8288 |
| `Cache-Control` | On `410` only | `Cache-Control: no-store` | — |
| `Location` | On `410` only, when `ReplacedBy` starts with `/` | `Location: /api/v2/books` | — (non-standard hint) |

Notes:

- `Sunset` is sent even after the block date, so a client holding a cached resource can still learn
  the retirement date from the `410` response itself.
- `Deprecation` carries the Unix timestamp of `WarnAfter`, not of `BlockAfter`: it tells when the
  endpoint became deprecated, which is the start of the warning phase.
- `Location` on a `410` is a convenience hint, not a redirect. The standards-compliant successor
  announcement is `Link rel="successor-version"`.
- The legacy `Warning: 299` header is intentionally not used.

## `410 Gone` response

With `EnforcementMode=Enforce` and `now ≥ BlockAfter`:

```http
HTTP/1.1 410 Gone
Content-Type: application/problem+json
Cache-Control: no-store
Sunset: Fri, 01 Jan 2027 00:00:00 GMT
Deprecation: @1798761600
Link: <https://docs.example.com/api-lifecycle>; rel="sunset"
Link: </api/v2/books>; rel="successor-version"
Location: /api/v2/books

{
  "type": "https://developer.mozilla.org/docs/Web/HTTP/Status/410",
  "title": "Endpoint has reached end of life",
  "status": 410,
  "detail": "This endpoint was permanently shut down on 2027-01-01. Migrate to: /api/v2/books.",
  "sunset": "2027-01-01",
  "replacement": "/api/v2/books",
  "traceId": "00-8f4c0d5b..."
}
```

- `HEAD` requests receive the same status and headers but no body.
- The body is produced by `GoneProblemDetailsFactory` when configured; see
  [Configuration](configuration.md#custom-410-payload).
- Blocked requests are still counted in metrics and logs, which is how you notice leftover
  consumers after enforcement is enabled.

## Route templates in metrics and diagnostics

Route templates are normalized to start with `/`. The template is the one declared by the endpoint,
so MVC constraints are preserved:

```text
/api/v1/books/{id:guid}
```

This keeps metric cardinality low: a concrete path such as `/api/v1/books/42` never becomes a tag
value.
