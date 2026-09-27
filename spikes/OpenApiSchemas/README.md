# Spike: can a schema transformer describe value objects in the built-in OpenAPI document?

**Question.** An earlier measurement found that an `IOpenApiSchemaTransformer` never reached a type
that only ever appears as a property, and repaired such types in a document transformer instead.
The current Microsoft docs say schema transformers run before schemas are hoisted into components.
Which is true for `Microsoft.AspNetCore.OpenApi` 10, and where does a value object appear in a
document at all: as a property, a list element, a dictionary value, a route or query parameter?

**Answer: one schema transformer suffices.** Measured 2026-09-27 with Microsoft.AspNetCore.OpenApi
10.0.12 (Microsoft.OpenApi 2.12), minimal APIs and MVC, OpenAPI 3.1 and 3.0, against value objects
woven by the real generators. The transformer reaches every value object in a JSON position,
including one that appears only as a property (finding 2). Two positions need more than "this
schema is a value object": container elements, which are dropped before any transformer runs, and
parameters, which reach the transformer typed as `string` (findings 4 and 6).

```bash
dotnet run --project spikes/OpenApiSchemas                      # every mode; documents in out/
dotnet run --project spikes/OpenApiSchemas -- observe --visits  # what the transformer is called with
dotnet run --project spikes/OpenApiSchemas -- candidate --v30   # OpenAPI 3.0
dotnet build spikes/OpenApiSchemas/Split/Api                     # finding 13: fails with ASP0020
dotnet build spikes/OpenApiSchemas/Split/Api -p:Pragma=true      # finding 17
dotnet build spikes/OpenApiSchemas/Split/Api -p:Suppress=true    # finding 14: still fails
dotnet build spikes/OpenApiSchemas/Split/Plain -p:Suppress=true  # finding 14: the control, CS0168 suppressed
```

## The candidate

A value object's schema becomes its wrapped type's schema, obtained from ASP.NET itself through
`context.GetOrCreateSchemaAsync(typeof(T))`, so it is whatever ASP.NET publishes for `T` under the
host's JSON options. Only the JSON Schema keywords are copied: ASP.NET's `Metadata` holds the
schema's reference id (finding 9). The type keeps its named component (`OrderId`), now
`{"type":"string","format":"uuid"}`.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | Without a transformer, a value object publishes the empty schema `{}` as a component; a list, array or dictionary of them loses `items`/`additionalProperties`; a parameter is a bare `string` | ✅ |
| 2 | A schema transformer is called for value objects that appear **only** as properties, and its change reaches the hoisted component. The earlier measurement does not hold in 10.0.12 | ✅ |
| 3 | A `Nullable<T>` property reaches it as `Nullable<T>`; the result is the component plus `oneOf: [null, $ref]` | ✅ |
| 4 | A container's element is never passed to the transformer: its schema is dropped first. The container is (`Kind` Enumerable or Dictionary, `ElementType` the value object) | ✅ |
| 5 | Setting the missing `items`/`additionalProperties` to `GetOrCreateSchemaAsync(elementType)` gives a `$ref` to the value object's component, for lists, arrays, dictionary values, nested containers, nullable elements and a query array | ✅ |
| 6 | A parameter bound through `TryParse` reaches the transformer as `String`. Minimal APIs name the value object in `ParameterDescription.Type`; MVC reports `Type` as `String` and names it in `ModelMetadata.ModelType`. `ParameterDescriptor` names the container for an MVC `[FromQuery]` object (for `[AsParameters]` it names the property), so it cannot be used | ✅ |
| 7 | With `Type`, then `ModelMetadata.ModelType`, every parameter shape (minimal route and query, `[AsParameters]`, MVC route and query, MVC `[FromQuery]` object) publishes exactly what a raw `Guid`/`int`/`long?`/`DateTimeOffset` parameter publishes | ✅ |
| 8 | `GetOrCreateSchemaAsync` applies the other schema transformers, whichever was registered first: a consumer's `Instant` transformer describes a value object wrapping `Instant` too | ✅ |
| 9 | Copying `Metadata` along with the keywords makes the position a reference to the wrapped type's component (`DayOfWeek`, `Instant`) and drops the value object's own | ✅ |
| 10 | The schema follows the host's JSON options, and so does the generated converter: under the web defaults a number is `integer \| string` with a pattern, and a quoted number is read (200) while `"abc"` is refused (400); with `JsonStringEnumConverter` an enum is its names, and the converter writes `"Monday"` | ✅ |
| 11 | OpenAPI 3.0: the same result; type arrays become `anyOf`, exactly as for the raw-type controls | ✅ |
| 12 | NodaTime, whose values the generated converter writes through NodaTime's own converters: with the host configured for NodaTime, the wrapped and the raw `Instant` both publish `{}`; without it, the host cannot read a raw `Instant` (400) and publishes it as `object` | ✅ |
| 13 | **ASP0020 (an error)** for a minimal-API **route** parameter whose value object is declared in the **same project**: the analyzer sees the source before Metalama weaves `IParsable` in. None from a referenced project, none for a query parameter, none in MVC | ✅ |
| 14 | A `DiagnosticSuppressor` has no effect under Metalama's compiler, not even on a plain warning (CS0168), while the same suppressor works in a project without Metalama (`Split/Plain`) | ✅ |
| 15 | The Request Delegate Generator intercepts the same endpoints and binds both value objects through `TryParse`; with ASP0020 silenced, binding works with reflection and with the generator (200, and 400 for a malformed id) | ✅ |
| 16 | A record class that wraps itself (`IValue<Self>`) compiles; describing it would recurse without end | ✅ |
| 17 | `#pragma warning disable ASP0020` around the endpoint does silence it under Metalama (`-p:Pragma=true`), as does `NoWarn` or an `.editorconfig` severity | ✅ |

## Consequences for the design

- **One schema transformer**, not a document transformer: it reaches every JSON position (finding 2),
  and it needs no list of types and no walk over the host's assemblies.
- **The schema is the wrapped type's, as ASP.NET describes it for this host** (findings 7, 8, 10).
  No format table of our own: `GetOrCreateSchemaAsync` already knows the host's number handling,
  enum converter and other transformers, and the generated converter reads and writes through the
  same options, so the document and the wire agree.
- **Containers**: fill in only a missing `items`/`additionalProperties` (finding 4). A schema
  ASP.NET managed to build is left alone.
- **Parameters**: `ParameterDescription.Type`, else `ModelMetadata.ModelType` (finding 6), and only
  when the transformer was handed something other than the wrapped type, which also ends the
  recursion through `GetOrCreateSchemaAsync`.
- **Keywords, not `Metadata`** (finding 9), and a cycle of value objects wrapping each other is an
  exception naming them, not a stack overflow (finding 16).
- **NodaTime** needs nothing of its own: a host that serves NodaTime values configures NodaTime's
  JSON converters and a schema transformer for them anyway, and that transformer then describes
  the value objects as well (findings 8, 12).
- **ASP0020** cannot be suppressed from the package (finding 14), and Metalama's own suppression is
  scoped to the aspect's targets, not to the endpoint code where ASP0020 is reported. It is
  documented instead: a value object used as a minimal-API route parameter lives in a project the
  endpoints reference, or the endpoint suppresses ASP0020 with a pragma (finding 17). The binding
  itself is right in every case (finding 15).
