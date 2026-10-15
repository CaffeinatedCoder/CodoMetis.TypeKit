# 0071. JSON converters say what they write; decimals get one spelling

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an application moving its JSON and OpenAPI code onto the package; refines
[decision 69](0069-plain-enum-schemas-left-to-host.md). Measured with System.Text.Json and
Microsoft.AspNetCore.OpenApi 10, osx-arm64, and under Native AOT in the smoke test's `aot` consumer.

**The gap.** A converter of one's own hides its type from every schema: System.Text.Json's exporter
writes `true`, ASP.NET writes `{}` or the bare `format` its CLR type maps to (`{"format":"double"}` for a
decimal), both drop `items` and `additionalProperties` for an element of one, and ASP.NET does it to a
route parameter too, which binds through `TryParse` and never touches JSON. System.Text.Json's hook for a
converter's schema is internal; the public proposal (dotnet/runtime#105769) is milestoned Future, and
ASP.NET defers to it (dotnet/aspnetcore#63051). The application had one transformer per converter for
this: decimals, NodaTime's thirteen types, enums.

**The mechanism**, in the base package, framework-neutral:
- `IDescribedJsonConverter.DescribeJson(type, options)` on a converter returns a `JsonSchemaDescription`:
  a JSON Schema of one non-null value (`Of`), complete in itself (`$ref`, `$defs`, `$id` refused), or the
  schema another type has (`SameAs`).
- `JsonSchemaDescriptions`, a converter factory that converts nothing, carries descriptions for
  converters the application does not own, by type or by a rule, inside `options.Converters`, so it
  travels wherever the options are copied and needs no wrapper around the converter. A wrapper was
  rejected: it strips a built-in converter of what the serializer gives its own alone, does not reach
  converters a library registers (`ConfigureForNodaTime`), and around a factory's converters needs a
  generic type made at run time, which Native AOT cannot make.
- `JsonSchemaDescription.For` answers in one order: the options' descriptions, the application's word;
  a described converter on the property; a value object, as `SameAs` its wrapped type, and a closed set,
  as a string of its names, each by the attribute the generators put on it; the options' converters as
  the serializer picks them, then the resolver's converter. A `SameAs` type that is described needs no
  metadata, so a source-generated context need not list what a value object wraps.
- `DescribedJsonSchema.ExporterOptions()` applies descriptions to System.Text.Json's exporter, repairing
  containers there too.

**In the OpenAPI document**, `AddTypeKit()` applies the same descriptions in every position (decision
69's line holds: the package writes what a converter or the host says, never a plain type's default),
and gives a non-body parameter back the schema the type has without converters of one's own.

**Two ways the document and the wire part, and what holds them.** ASP.NET builds the document from the
minimal-API JSON options, and MVC writes with its own: a converter added through `AddJsonOptions` alone
wrote `{"amount":"7.8"}` under a document of numbers, without a word (measured in a review the same day).
Where the host has a controller, building the document fails when the two option sets do not carry the
same described converters and description sets, by type, naming each that differs; a host of minimal APIs
alone is never compared. And an entry in `JsonSchemaDescriptions` comes before a converter's own
description, so a host can refine one, a `minimum` on a value object, and can also contradict one,
describing as strings what is written as numbers. That is not refused: nothing tells a refinement from a
contradiction, and the entry is the application's word on its own wire.

**Decimals** are its first use. `DecimalNumberJsonConverter` and `DecimalStringJsonConverter` write the
smallest scale (division by a scale-28 one; `ToString("G29")` writes `1E-07`), a zero of any scale or sign
as `0`, as a value and as a dictionary key, which a converter of one's own must write itself or the
serializer's keeps the scale. Both read a number or a string, the serializer's own refusal for anything
else. Each describes what it writes: `{"type":"number","format":"decimal"}`, and a string with the
minimal spelling's pattern. Rejected: `JsonNumberHandling.WriteAsString`, which turns every number into a
string and normalises nothing; a scale per type, which widens `7.8` to `7.8000`. A negative zero is
written `0.00` by the serializer on .NET 10, not `-0`; the zero guard is for the scale.

Not in the package: a NodaTime or an enum description of its own. Those stay the application's word, a
few lines in its options. A fixed NodaTime description set would be wrong for a host that changes
NodaTime's patterns (an `Interval` as an object or an ISO string; a `Duration` in NodaTime's own pattern),
and a satellite would set NodaTime's version floors and carry its converters' trimming warnings, so a
NodaTime satellite that registers converters and descriptions in one call is a spike first (roadmap).
