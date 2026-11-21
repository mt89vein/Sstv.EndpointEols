# Documentation

`Sstv.EndpointEol` makes endpoint retirement a first-class, observable process.

| Document | Contents |
|---|---|
| [Getting started](getting-started.md) | Install, register, mark endpoints and place the middleware. |
| [Configuration](configuration.md) | Every option, enforcement modes and custom error payloads. |
| [HTTP contract](http-contract.md) | Phases, headers and the `410 Gone` response. |
| [Metrics](metrics.md) | OpenTelemetry instruments, tags, consumer tracking and PromQL examples. |
| [Diagnostics API](diagnostics.md) | JSON report of all sunsetted endpoints. |
| [Swagger / OpenAPI](swagger.md) | Annotations from the `Sstv.EndpointEol.Swashbuckle` package. |
| [Rollout guide](rollout.md) | A safe path from announcement to removal. |
| [Sample application](sample.md) | Run the sample and try every phase yourself. |
| [Design notes](design.md) | Architecture, decisions, limitations and alternatives. |

## Package split

| Package | What it contains |
|---|---|
| `Sstv.EndpointEol` | Runtime: `[EndOfLife]`, middleware, metrics, diagnostics, startup validation. No Swashbuckle dependency. |
| `Sstv.EndpointEol.Swashbuckle` | `IOperationFilter` and `IDocumentFilter` that annotate OpenAPI documents. |

Applications that do not use Swashbuckle only need the core package.
