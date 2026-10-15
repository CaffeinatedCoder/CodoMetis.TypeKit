# 0083. A description asks the contract's converter first, and an `Enum` bound proves nothing against `struct`

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a last external review of the unreleased 2.1.0 across its 34 commits since 2.0.0, three
defects and two observations, each reproduced before it was fixed; refines the lookup order of
[decision 74](0074-descriptions-resolve-the-converter-as-the-serializer-does.md) and the `[Distinct]`
proofs of [decision 79](0079-distinct-and-unrelated-type-parameters.md). Measured with System.Text.Json 10
and Microsoft.OpenApi 2.12.

**The contract's converter comes first.** Decision 74 asked the options' converters before the resolver,
which is the serializer's order under the reflection resolver, whose contract weighs them first. A
resolver of one's own decides the contract alone: with `DecimalStringJsonConverter` in the options and a
resolver putting a number converter in charge of `decimal`, the wire was `1.2` and the description a
string. `JsonSchemaDescription.For` now asks the contract the options resolve (`TryGetTypeInfo`), as a
value object's converter reaches its wrapped type (`GeneratedJson.TypeInfo`), and falls back to the
options' converters, then the generated converter, only for a type the resolver does not know, which a
source-generated context need not list.

**A `Nullable<T>`'s converter is its own unless it is the serializer's.** `For` unwrapped a nullable type
before the resolver was asked, so a resolver's described converter for a nullable value object, which wrote
`"cents:780"`, was passed over for the wrapped decimal, and the refusal for a nullable position of a shared
component, which compares the two, never fired. The nullable contract is asked first now. Its converter is
seen through only where it is System.Text.Json's own adapter, which writes null or what the contract's
converter for `T` writes: the reflection resolver builds it from `T`'s contract (measured: under the
conflicting resolver above, `decimal?` wrote `1.2` too), and a source-generated context from the `T` it
lists. The adapter is recognised as a converter of the nullable type from System.Text.Json's assembly,
which has no other; a named type would be internal. Rejected: reading the adapter's element converter, a private field, which
trimming and Native AOT do not keep.

**The reference check asks the contract too.** Under `ReferenceHandler.Preserve` a value object is refused
where its wrapped type may write an `$id` (`GeneratedJsonPlan`), and a nullable struct was walked as the
struct it holds unless the options listed a converter for it, the same early lookup. A resolver's converter
for `HoldingTracked?` that wrote a string was refused, and a resolver that put the serializer's own converter
in charge over one in the options let the class inside write its `$id` twice, unrefused. Both now ask the
contract's converter, through the one adapter check the description uses (`GeneratedJson.IsNullableAdapter`).

**An `Enum` bound proves nothing against `struct`.** For `[Distinct]`, `struct` or `unmanaged` against any
class bound was taken as proof that no type meets both, but every enum meets `where T : Enum`: a wrapper
`Forward<TS, TE> where TS : struct where TE : Enum` held `Fault<TS, TE>` without CMTK0010, and
`Forward<DayOfWeek, DayOfWeek>` compiled. The bound must now be one no value type boxes to, the rule
`[Unrelated]` already applied. `ValueType` cannot be a bound (CS0702). Fixed before the tag: CMTK0010's pair
checks ship in 2.1.0, and an Error that later caught more would break builds within the major
([decision 34](0034-errors-fixed-within-major-version-warnings.md)).

**A mixed pair forwards only what both declare.** One `[Unrelated]` and one `[Distinct]` are held to
`[Unrelated]` at their closed uses, but into an `[Unrelated]` pair they are not proved, since the
attribute must be on both for its closed uses to check the relation; into a `[Distinct]` pair they are.
Kept as it was, conservative, now pinned by a test and said in both proof tables.

**An empty keyword that restricts nothing is no change.** `"properties": {}` was refused as a schema the
document would change, since Microsoft.OpenApi leaves it out; so are an empty `patternProperties`,
`dependentSchemas`, `dependentRequired` and `required` (measured). Each restricts nothing empty, and the
comparison treats it as the default the writer omits, as it does a `false` annotation. An empty `enum`
admits nothing and is no default.
