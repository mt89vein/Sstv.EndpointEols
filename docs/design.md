# Design notes

This document explains how the library works, why it is shaped this way, and which alternatives
were considered.

## Problem

Retiring an API endpoint is usually ad-hoc. Someone deletes the action, clients start receiving
`404 Not Found`, and nobody knows who was still calling it. The three questions that matter during
a shutdown are:

1. **When** will the endpoint stop working?
2. **Who** is still using it, and how much?
3. **What** should consumers migrate to?

HTTP standards already cover the announcement (`Sunset` — RFC 8594, `Deprecation` — RFC 9745), but
wiring dates, headers, logging, metrics, blocking and documentation per endpoint is repetitive and
drifts out of sync. The goal is to make the lifecycle declarative, observable and safe to enforce.

## Goals

- Declare a schedule next to the endpoint code, subject to code review.
- Announce the schedule with standard HTTP headers.
- Measure real usage during a warning phase before blocking anything.
- Make blocking a configuration decision, reversible without a redeploy.
- Expose the whole retirement schedule for humans and tooling.
- Fail fast on invalid configuration.
- Support MVC controllers and Minimal APIs equally.

## Non-goals

- API versioning as a whole (see [alternatives](#alternatives)).
- Gateway-level traffic management.
- Authentication or authorization of deprecated endpoints.
- Per-tenant or per-plan schedules. The attribute describes the endpoint, not the caller.

## Phases

| Phase | Condition | Behaviour |
|---|---|---|
| Active | `now < WarnAfter` | Normal response, `Sunset` announced, `phase="active"` counted. |
| Warning | `WarnAfter ≤ now < BlockAfter` | Normal response, `Deprecation` added, warning log, `phase="warning"` counted. |
| Blocked | `now ≥ BlockAfter` | `phase="blocked"` counted; `410 Gone` only in `Enforce` mode. |

The warning phase is the core of the design. Blocking is mechanical; knowing whether anyone still
depends on the endpoint is the hard part.

## Architecture

```text
                    ┌──────────────────────────────┐
   [EndOfLife] ───► │ Endpoint metadata            │
   WithEndOfLife    └──────────────┬───────────────┘
                                   │
        ┌──────────────────────────┼────────────────────────────┐
        ▼                          ▼                            ▼
 EndOfLifeMiddleware        EndpointEolRegistry        Sstv.EndpointEol.Swashbuckle
 headers, phases,           diagnostics snapshot,      operation + document filters
 logs, metrics, 410         startup validation
```

### `EndOfLifeAttribute`

A plain attribute holding ISO dates and an optional replacement hint. Attribute arguments must be
compile-time constants, so `DateOnly` cannot be used; strings are parsed and validated once.

### `EndOfLifePolicy`

The parsed, validated schedule. It calculates the phase for a given date and applies the 90-day
default warning window when `WarnAfter` is omitted. Parsing is centralized so the middleware,
registry and Swagger filters cannot disagree.

### `EndOfLifeMiddleware`

Placed after routing, it reads the selected endpoint, resolves the effective attribute
(action-level overrides controller-level), and:

- adds headers;
- counts the request in every phase;
- tracks distinct consumers outside the active phase;
- logs warnings with per-consumer throttling;
- returns `410 Gone` when the endpoint is blocked and enforcement is on.

It is a middleware rather than an MVC action filter because it must work for both MVC and Minimal
APIs and reject blocked requests as early and cheaply as possible. `EnforcementMode` is read from
`IOptionsMonitor` on every request, so the kill switch does not require a restart.

### `EndpointEolRegistry`

Builds a snapshot of all endpoints with `[EndOfLife]` metadata from `EndpointDataSource`. The list is
cached and invalidated by routing change tokens. Volatile fields (`phase`, `daysToBlock`) are
recalculated on every read, so a cached snapshot never reports stale phases. The same snapshot is
used by the diagnostics endpoint and by startup validation.

Route templates are normalized to start with `/`. The declared template is preserved, including MVC
constraints such as `{id:guid}`, which keeps metric cardinality low.

### Diagnostics endpoint

A Minimal API endpoint (`MapEndOfLifeDiagnostics`) rather than a controller, so the core package does
not depend on MVC. It returns the registry snapshot together with `serverTime` and the active
enforcement mode.

### Startup validation

A hosted lifecycle service validates every marked endpoint and logs the schedule once. Validation
runs in `StartedAsync`, after the web host has built the routing pipeline. This is deliberate: an
`IStartupFilter` can run before controller endpoints are materialized, in which case validation would
silently see zero endpoints.

### Swashbuckle package

The OpenAPI filters are isolated in `Sstv.EndpointEol.Swashbuckle`. The core package stays free of
Swashbuckle and `Microsoft.OpenApi` dependencies, which matters for Minimal API applications that do
not generate OpenAPI documents with Swashbuckle.

## Key decisions

| Decision | Rationale |
|---|---|
| Attribute, not configuration | The declaration lives next to the code, is reviewed with the endpoint and cannot drift from it. |
| Dates as `yyyy-MM-dd` strings | Required by C# attribute constant rules. Parsed once, validated at startup. |
| `410 Gone`, not `404` or `403` | `410` is explicit that the resource existed and was intentionally removed, and it supports a migration hint. |
| Standard headers | Proxies, clients and generators already understand `Sunset`, `Deprecation` and `Link`. |
| Warning phase with metrics | Answers "who still uses this?" before anything breaks. |
| Route template as metric tag | Prevents unbounded cardinality from path parameters. |
| `WarnOnly` as default | The feature can be shipped everywhere without risking an outage. |
| Kill switch read per request | Emergency rollback without a release. |
| Consumers counter | A warning log alone shows only the last caller; a distinct-consumer count shows the real surface. |
| `TimeProvider` | Phase boundaries are testable with `FakeTimeProvider`. |
| Separate Swashbuckle package | Keeps the runtime dependency-free and lets non-Swagger apps opt out. |

## Alternatives

### API versioning as a whole

`Asp.Versioning.Mvc` deprecates an entire version (`/v1`) rather than individual operations and has
built-in sunset policies. It is a better fit when the API surface is large and consumers migrate by
version. Per-endpoint EOL remains useful for targeted retirements and can coexist with versioning.

### Gateway-level governance

Azure API Management, Kong, Apigee, Tyk or a YARP gateway can enforce quotas, blocking and headers
centrally, and dates can change without a deployment. This is a better fit for many services behind
a shared gateway. With no gateway, application middleware is the pragmatic option. If a gateway
appears later, enforcement can move there while the application keeps the headers and annotations.

### Configuration-driven registry

Dates in `appsettings` or a database are more flexible but lose the main benefit of the attribute:
the declaration sits next to the endpoint and goes through code review. A hybrid override layer adds
complexity that is hard to justify for a single service.

### Contract-first governance

Spectral rules in CI (for example, "every deprecated operation must have `x-sunset` and a
replacement") and consumer-driven contract tests (Pact) are excellent complements. They do not
replace runtime enforcement but make the contract visible earlier.

### `[Obsolete]` and a Roslyn analyzer

Useful for internal C# consumers of a shared SDK, not for HTTP endpoints.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Blocking a client that did not migrate | Warning phase, consumer metrics, `WarnOnly` default, per-request kill switch. |
| Metric cardinality explosion | Tag is the route template, not the raw path. |
| Forgetting to remove dead code | Diagnostics report and startup log show the schedule; blocked traffic is measurable. |
| Accidentally marking infrastructure endpoints | The attribute is explicit; health checks and Swagger are not marked automatically. |
| Changing dates requires a release | Intentional: dates are part of the API contract. Emergencies use the kill switch. |
| Startup validation missing controller endpoints | Validation runs after the routing pipeline is built and touches MVC action descriptors first. |
| Proxy stripping custom headers | `Sunset` and `Deprecation` are standard and rarely stripped; the `410` body repeats the information. |

## Acceptance criteria

- Active phase: normal response plus `Sunset`/`Link`.
- Warning phase: the same plus `Deprecation`, a warning log and `phase="warning"` metric.
- Blocked phase in `Enforce`: `410` with ProblemDetails and headers, `phase="blocked"` metric.
- `WarnOnly` never blocks.
- Diagnostics returns the schedule sorted by block date.
- Swagger contains `deprecated: true` and `x-sunset` for marked operations.
- Invalid attributes fail startup with a descriptive error.
- Unit tests use `FakeTimeProvider`; integration tests exercise the sample application through a
  real HTTP pipeline with `WebApplicationFactory`.
