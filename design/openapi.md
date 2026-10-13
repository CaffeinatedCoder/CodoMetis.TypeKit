# OpenAPI

**Decided 2026-09-27, measured in [spikes/OpenApiSchemas](../spikes/OpenApiSchemas/README.md)**
with Microsoft.AspNetCore.OpenApi 10.0.12. An earlier measurement had found that a schema
transformer never reached a type that only appears as a property, and repaired such types in a
document transformer. It does not hold in 10.0.12: one schema transformer reaches every JSON
position, and the change lands in the hoisted component.

- **The schema is the wrapped type's, as ASP.NET publishes it for this host**, asked of ASP.NET
  through `context.GetOrCreateSchemaAsync(typeof(T))`. It therefore follows the host's JSON options
  (number handling, enum converter) exactly as the generated converter does, and the document's
  other transformers (a NodaTime transformer describes a value object wrapping `Instant` too). No
  format table of our own.
- **Keywords are filled in, never the object replaced**: the transformer receives the instance ASP.NET
  hoists into the value object's own component, so `OrderId` stays a named component. What the
  value object's schema already says is kept. ASP.NET's `Metadata` (the reference id) and the
  schema-identity keywords are never copied; copying `Metadata` made every use a reference to the
  wrapped type's component. A keyword is assigned only when the wrapped schema has one.
- **Containers**: ASP.NET drops `items`/`additionalProperties` for a converter-backed element before
  any transformer runs; the value object's own schema is put back, only where missing.
- **Parameters** bound through the generated `TryParse` reach the transformer as `string`, ASP.NET's
  placeholder for any parsable type, which the wrapped type's schema replaces. Minimal APIs name the
  value object in the parameter's `Type`, MVC in `ModelMetadata.ModelType`; the parameter descriptor
  names the container for an MVC `[FromQuery]` object and is not used. Each parameter then publishes
  exactly what a parameter of the wrapped type publishes.
- **A value object that reaches itself** through what it wraps is an `InvalidOperationException`
  naming the chain, not a stack overflow.
- **Source-generated JSON** ([Native AOT](native-aot.md)): ASP.NET builds the wrapped type's schema from the host's JSON
  contract for it, and a source-generated context has none for a type the host never serializes.
  The transformer then refuses with an `InvalidOperationException` naming the value object and the
  `[JsonSerializable(typeof(Guid))]` to add, rather than the serializer's message about a Guid. A
  resolver of our own cannot be added: the host's options are read-only by then.
- **Component names (decided 2026-09-28, [decision 24](adr/0024-nested-value-objects-named-after-nesting.md)).** ASP.NET names a component after the type's
  simple name, so `Shop.Id` and `Stock.Id` shared one, and a stock id wrapping an `int` was documented
  as a uuid. `AddTypeKit()` wraps `CreateSchemaReferenceId`: a nested value object that got ASP.NET's
  default name is named after its nesting chain (`ShopId`, as its companion class is `ShopIdExtensions`);
  a name the host's own delegate chose, or `null` for an inlined value object, is kept. A name that
  is also another type's, `Order.Status` beside a top-level `OrderStatus`, refuses the document
  ([decision 55](adr/0055-component-name-claimed-twice-refused.md)).
- **Fault responses** ([decision 49](adr/0049-fault-responses-client-can-act.md)): an operation
  transformer adds a problem response per status an endpoint answers its fault type with, referring to
  the problem schema the host's `CreateSchemaReferenceId` names (the shared `ProblemDetails`
  component, or an inline schema). That schema gains an optional `fault` string, the member a client
  matches on, whose description says which form the host writes (`QualifiedFaultNames`) and that only
  a problem answering a fault carries it; what a schema already says of `fault` is kept. ASP.NET's own
  problem responses refer to the same component (measured, 10.0.12: whichever endpoint comes first,
  and a second document without a fault response keeps it bare); an inline schema of theirs is another
  instance and stays bare. A document without a fault response is ASP.NET's, byte for byte.
- **Known gaps, documented rather than fixed:** a validation attribute on a value-object property
  lands in the shared component, as ASP.NET does for any referenced schema (rules belong in `Create`);
  a property absent from a request body stays `default`, since System.Text.Json calls no converter
  for it (`required`, `RespectRequiredConstructorParameters`); a query, header or form value absent
  from an MVC request reaches the action as `default` for a struct value object, MVC's rule for any
  non-nullable struct, where a minimal API answers 400 (`[BindRequired]`, or a nullable parameter;
  measured 2026-10-13, MvcBindingTests); a minimal API answers a refused body
  with an empty 400; MVC's own binding message quotes the input, as it does for a `Guid`.
- **ASP0020** ([decision 12](adr/0012-asp0020-documented-suppressed.md)): a minimal-API route parameter whose value object is declared in the
  same project fails the build, because the route analyzer reads the source before Metalama weaves
  `IParsable` in. Binding is correct at run time and in the request delegate generator. A
  `DiagnosticSuppressor` has no effect under Metalama's compiler, and Metalama's own suppression is
  scoped to the aspect's targets, so the package README documents the pragma instead.

Vogen 8.0.7 generates a schema transformer for Microsoft.AspNetCore.OpenApi too, from a table of
wrapped types (checked in its source at the tag, 2026-09-28; the 2026-09-27 note here said
Swashbuckle only). The README describes what the package does and claims nothing about other
libraries.
