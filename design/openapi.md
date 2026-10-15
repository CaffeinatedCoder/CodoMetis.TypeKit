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
- **Described types** ([decision 71](adr/0071-converters-say-what-they-write.md)): a converter that
  implements `IDescribedJsonConverter`, or a type the host's JSON options describe with
  `JsonSchemaDescriptions`, is described as that says by `DescribedSchemaTransformer`, registered after
  the value-object and closed-set transformers so a host's description of a value object replaces their
  shape (a reordering is a failing test). ASP.NET writes a converter of one's own as `{}` or a bare
  `format` and drops `items`/`additionalProperties` for an element of one (measured, 10.0); the
  description goes in every JSON position and the containers are repaired where they lack one. A body is
  a parameter too, and every schema in one; only a non-body parameter binds through `TryParse`, and it
  gets back the schema the type has without converters of one's own, beside ASP.NET's `format`. A
  description is parsed by `OpenApiModelFactory.Parse<OpenApiSchema>` (Microsoft.OpenApi 2.0 on), measured
  to survive Native AOT trimming in the smoke test's `aot` consumer, then written back in 3.1 and compared
  with what was described: a keyword the model does not hold is written aside as unrecognised, where no
  validator sees it, so a difference fails the document ([decision 73](adr/0073-schema-descriptions-follow-the-serializer.md)).
  A document below 3.1 is written in its own version too, and a described keyword that reaches it only as
  an `x-jsonschema-` extension or not at all (`if`, `contains`, `patternProperties`, `dependentRequired`)
  fails it naming the keyword; 3.0's own spellings (`nullable`, `anyOf` of types, `enum` for a `const`)
  are kept ([decision 74](adr/0074-descriptions-resolve-the-converter-as-the-serializer-does.md)). Where the host has a controller,
  `DescribedOptionsAgreement` fails the document when MVC's JSON options describe a type the document
  holds otherwise than the minimal-API ones, a patch's argument included, compared by what
  `JsonSchemaDescription.For` answers under each: the document is built from the latter, and MVC writes
  with the former. `For` resolves the converter as the serializer does, a factory for the converter it
  makes and a `Nullable<T>` before `T`. A described converter one property names for a type with a
  reference id fails the document as well, and so does one registered for the `Nullable<T>` of such a type
  alone: the component is written once, for every position.
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
  instance and stays bare. A document without a fault response is ASP.NET's, byte for byte. An
  endpoint answering several fault types, a `Fault<T1, T2>`'s each, gets one response per status,
  ascending, listing the problem types of every type at a status they share; the responses the endpoint
  declares itself take their places among them (a range such as `4XX` after its codes, `default` last),
  and an operation without a fault response keeps ASP.NET's order; a member the endpoint
  leaves out (`WithoutFaults`) gives it no status and no problem type
  ([decision 78](adr/0078-fault-sets-compose-side-by-side.md)).
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
