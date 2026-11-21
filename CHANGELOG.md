# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-21

Initial public release.

### Added

- `[EndOfLife]` attribute for MVC actions and controllers, plus `WithEndOfLife(...)` for Minimal API
  endpoints. Action-level attributes override controller-level ones.
- Standard HTTP headers on marked endpoints: `Sunset` (RFC 8594), `Deprecation` (RFC 9745),
  `Link rel="sunset"` and `Link rel="successor-version"`.
- `410 Gone` responses with `application/problem+json`, `Cache-Control: no-store` and a `Location`
  migration hint once an endpoint is blocked.
- Three lifecycle phases (`Active`, `Warning`, `Blocked`) driven by `TimeProvider`, so they can be
  tested with a fake clock.
- Four enforcement modes (`Off`, `MetricsOnly`, `WarnOnly`, `Enforce`) read per request, so the kill
  switch works without a restart or redeploy.
- OpenTelemetry metrics: `eol.requests{route, method, phase}` and `eol.consumers{route}`.
- Structured warning logs throttled per route, phase and consumer.
- Diagnostics endpoint via `MapEndOfLifeDiagnostics(...)`, listing every sunsetted API sorted by
  block date.
- Fail-fast startup validation of marked endpoints and a one-time startup schedule log.
- Optional `Sstv.EndpointEol.Swashbuckle` package adding `deprecated: true`, sunset notes,
  `x-sunset` vendor extensions, clickable deep links to the replacement operation and a dedicated
  `Deprecated / EOL` tag to OpenAPI documents.
- Runnable sample application with Swagger UI and integration tests over the sample using
  `WebApplicationFactory`.

[1.0.0]: https://github.com/sstv/Sstv.EndpointEol/releases/tag/v1.0.0
