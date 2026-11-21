# Swagger / OpenAPI

Swashbuckle integration lives in the separate `Sstv.EndpointEol.Swashbuckle` package, so the core
runtime package has no Swagger dependency.

```bash
dotnet add package Sstv.EndpointEol.Swashbuckle
```

## Registration

Automatic:

```csharp
builder.Services.AddEndOfLifeSwagger();
builder.Services.AddSwaggerGen();
```

The order of the two calls does not matter. `AddEndOfLifeSwagger` registers a post-configuration
step and is idempotent.

Manual, if you prefer to configure Swagger generation explicitly:

```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.AddEndOfLifeAnnotations();
});
```

Both the operation filter and the document filter are public, so they can also be registered
directly:

```csharp
options.OperationFilter<EndOfLifeOperationFilter>();
options.DocumentFilter<EndOfLifeDocumentFilter>();
```

## What is added

For every operation whose endpoint is marked with `[EndOfLife]` or `WithEndOfLife`:

1. `deprecated: true` — Swagger UI renders the operation struck through.
2. A human-readable note appended to the description, with the replacement as a clickable link
   when it can be resolved:

   ```text
   **Deprecated.** Planned removal (sunset): **2027-01-01**. Warnings begin: 2026-01-01. Replacement: [`/api/v2/books/{id}`](#/Books/delete_api_v2_books__id_).
   ```

3. Vendor extensions for machine processing (client generators, linters):

   | Extension | Value |
   |---|---|
   | `x-sunset` | `BlockAfter` date |
   | `x-eol-warn-after` | `WarnAfter` date |
   | `x-eol-replaced-by` | Replacement hint |

4. The `Deprecated / EOL` tag, placed **first** in the document so the whole list of sunsetted APIs
   is visible without filtering. Original tags are preserved, so each operation still appears under
   its resource group as well.

## Replacement deep links

When `ReplacedBy` points to an operation that is part of the same document, the replacement note
becomes a link that expands and scrolls to that operation inside Swagger UI. This is the Swagger UI
deep link format `#/{tag}/{operationId}`.

```csharp
[EndOfLife("2027-01-01", WarnAfter = "2026-01-01", ReplacedBy = "/api/v2/books/{id}")]
```

Clicking the link in the deprecated operation jumps to the successor, so consumers can inspect or
try out the new endpoint without searching for it manually.

### How the target is resolved

- The value is matched against the document paths. A leading HTTP method is optional and lets you
  pick one operation when a path has several:

  ```csharp
  ReplacedBy = "DELETE /api/v2/books/{id}"
  ```

- Route constraints are ignored during matching, so `ReplacedBy = "/api/v2/books/{id:guid}"` finds
  the OpenAPI path `/api/v2/books/{id}`.
- A path-only replacement is linked only when it is unambiguous: the operation with the same HTTP
  method wins, and a path with exactly one operation is also accepted. If several operations match
  and the method cannot decide, the note stays plain code instead of pointing at the wrong endpoint.
- Absolute `http`/`https` URLs are rendered as external links (useful for migration guides).
- Anything that cannot be resolved is left as inline code, so the document never contains a dead
  link.

### operationId

Swagger UI deep links address an operation by its `operationId`. If the successor operation has no
`operationId`, one is generated and persisted in the document:

```text
delete_api_v2_books__id_
```

Existing `operationId` values (for example from `[SwaggerOperation]` or `CustomOperationIds`) are
never overwritten. The generated format matches the fallback Swagger UI itself uses, so the anchor
is stable even if the `operationId` is removed later.

### Requirements

Deep linking is a Swagger UI feature and is enabled by default (`deepLinking: true`). The links open
in a new browser tab; the document itself remains a standard OpenAPI document, and the link is only
Markdown in the description plus the usual `x-eol-*` extensions.

## MVC and Minimal APIs

- Controller actions and controllers are discovered through reflection.
- Minimal API endpoints are discovered through endpoint metadata added by `WithEndOfLife`, which is
  not visible to reflection.

An action-level attribute overrides a controller-level one, exactly as in the runtime middleware.

## Example

The sample application registers the integration and exposes the generated document at
`/swagger/v1/swagger.json` with the UI at `/swagger`. See [Sample application](sample.md).
