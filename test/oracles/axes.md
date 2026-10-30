# What a consumer varies

The claims say what is promised, and the surface says what is offered. Neither says which settings a consumer
combines: the JSON options of the host, the OpenAPI version, MVC or a minimal API, the EF provider, the shape of
their own type. This is the hand-written list (design/adr/0112): one section per surface family, each axis with
its values, the tests that already vary it, and the pairs no test varies yet, which are the next probe class.

A **probe class** (`*ProbeTests`) is an ordinary test over the full product of a few axes, in the project that
already has the harness. Its oracle is the document's promise, it ends in `Candidates.Verdict`, and it is
registered in `oracles.json` with a seed like every other oracle. It stays under a minute: nothing replays it by
footprint, it runs with the suite.

## The oracles this repository promises

- **JSON parity (differential).** A shape with a value object and the same shape with the wrapped type serialize
  to the same bytes, as a value, a property and a dictionary key. `JsonParityTests`.
- **Schema parity (differential).** The OpenAPI schema of a value-object property equals the wrapped type's, with
  value objects as components and inlined (`InlineValueObjects`). `SchemaTests`, `NullablePositionTests`.
- **Column parity (differential).** The EF model gives a value-object property the wrapped type's column, and a
  key the wrapped type's generation. `MappingTests`, `KeyGenerationVariantTests`, `ColumnFacetVariantTests`.
- **One set of rules (differential).** `TryFrom`, `Parse`, the JSON converter, the type converter and the
  binders refuse what `Create` refuses. `EntryPointValidationTests`.
- **Arithmetic parity (differential).** A value object's generated arithmetic operator gives what the wrapped
  type's own gives, value or exception, unchecked and checked, for every numeric wrapped type and both record
  shapes. `ArithmeticParityTests`.
- **No refusal quotes the input (exception).** A sentinel in refused input never reaches a message.
  `UnreadableInputTests`.
- **Off the wire (exception).** `Option` and `Result` refuse System.Text.Json in both directions. `NotWireTypeTests`.
- **Every member, by reflection (completeness).** A null delegate on every branch, an uninitialized result, the
  context an asynchronous callback runs on. `NullDelegateTests`, `UninitializedResultTests`, `AsynchronousContextTests`.

## The axes

| Family | Axis | Values | Varied by | Pairs no test varies yet |
|---|---|---|---|---|
| JSON | the host's options | defaults, Web, a naming policy, a dictionary-key policy, number handling, a host converter | `JsonParityTests` | reference preservation beside a dictionary key |
| JSON | the wrapped type | every built-in the generators wrap, nullable or not | `JsonParityTests`, `JsonTests` | |
| OpenAPI | the version | 3.1, 3.0, 2.0 | `SchemaTests`, `FaultResponseTests` | a withheld fault name under 2.0 |
| OpenAPI | where value objects are described | components, inlined by `InlineValueObjects`, everything inlined by the host | `SchemaTests`, `NullablePositionTests`, `DescribedParameterTests` | `InlineValueObjects` beside a described type's parameter (`DescribedParameterTests` inlines by the host's own delegate) |
| HTTP | the host | minimal API, MVC with `[ApiController]`, MVC without | `MvcBindingTests`, `FaultStatusScopeTests` | |
| EF | the provider | SQLite, Npgsql, SQL Server | `KeyGenerationVariantTests`, `QueryTranslationVariantTests` | |
| EF | where a key sits | `KeyShapes` (`test/CodoMetis.TypeKit.EntityFrameworkCore.Tests/Variants/`) | `KeyGenerationVariantTests` | |
| EF | the query | `QueryShapes` | `QueryTranslationVariantTests` | |
| Analyzers | where a type argument is written, and what it is | `TypeArgumentSites`, `TypeArgumentKinds` (`test/CodoMetis.TypeKit.Analyzers.Tests/Variants/`) | `TypeArgumentConstraintVariantTests` | |
| Generation | the declaration's shape | record struct, record class, struct, class; sealed or not; over each wrapped kind | `BuildOutcomeTests`, `GeneratedSurfaceTests` | |
| Generation | the operator interfaces a plain value object declares | each arithmetic interface, the other operand and the result as the value object or another type, declared directly or through another interface, written by hand (public, explicit, a base record's explicit, an interface's explicit) or not; over every numeric wrapped type and both record shapes | `ArithmeticParityTests`, `ArithmeticTests`, `ArithmeticBuildTests` | an operator interface beside a hand-written `CompareTo` |

An empty last column means nobody has looked yet, not that nothing is missing. Fill it when a feature lands or a
finding names a combination no test varied.
