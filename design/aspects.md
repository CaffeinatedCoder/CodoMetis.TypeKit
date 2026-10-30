# Aspects

- **Aspect-class count.** Twelve classes: eleven for value objects and `ClosedSetAspect`, whose design is
  its own note ([Closed sets](closed-sets.md)). The Metalama Open Source edition (MIT) has no aspect-class
  limit (pricing page, 2026-09-26). The unlicensed CI build confirms it (phase 6).
- **Entry points.** `ValueObjectAspect` introduces three private helpers on every value
  object, and they are the only way the JSON, parsing and type-converter aspects create an
  instance: `__FromJson` (a refusal throws `JsonException`), `__FromText` (`FormatException`) and
  `__TryFromText` (`false`). A plain value object constructs directly; a validated one calls
  `Create` through `GeneratedFactories` in the base package. The exception is the explicit
  `IValueObjectMaterializer.Materialize`, by contract. `MinValue`/`MaxValue` are only generated for
  a plain value object, and so are arithmetic operators, which wrap their result through the private
  constructor as `From` does.
- **Arithmetic is opt-in (decided 2026-10-30, [decision 121](adr/0121-arithmetic-operators-are-opt-in-and-a-hand-written-one-is-a-seam.md)).**
  `ValueObjectArithmeticAspect` generates the operators of the operator interfaces a plain value object
  declares, where the wrapped type implements the same interface with the value object replaced by itself,
  each calling the wrapped type's through `GeneratedArithmetic`, a constrained generic call with its context
  stated. A hand-written operator for the same operands, or an explicit implementation of the interface, its
  own or a base record's, is a seam: kept with its `checked` form, never CMTK1011, so code that wrote them on
  2.3 builds unchanged. Each generated operator carries `[TranslatedAsWrappedArithmetic]`, which is how an EF
  Core query tells it from a hand-written one ([decision 122](adr/0122-a-hand-written-operator-is-refused-in-a-query.md)).
- **Declarations that cannot be generated** are errors, so no type is left half-generated:
  CMTK1000/1001/1002 (not `partial`, not a record, a struct not `readonly`), CMTK1003 (more than one
  marker), CMTK1004 (a validated marker whose first type argument is another type), CMTK1005 (a
  generic value object, one that derives from another value object, a wrapped type that is not
  a class, struct or enum, or one that is a value object: itself, one that reaches it again, or
  any other, see below), CMTK1006 (a record class that is not `sealed`: a derived record compares
  equal only to its own type, which is not value equality), CMTK1007 (the name of the
  `GetValue`/`ValueOrNull` class is taken, see below), CMTK1008 (a comparison member beside the
  seam, see below), CMTK1009 (a hand-written constructor, see below), CMTK1022 (an abstract type,
  reported by the fabric, [decision 54](adr/0054-abstract-value-object-refused.md)).
- **No hand-written constructor (decided 2026-09-27).** A value object's one constructor is the
  generated private one, and a hand-written instance constructor is CMTK1009. The implementation
  aspect answers it before introducing its own, since it reads only its target: `Constructors` minus
  the implicitly declared ones, measured to catch every form (the generated one's signature, any
  other, one chained to the generated one, a struct's `X()`, a record's copy constructor, a
  positional record's parameter list) and to leave the compiler's own and a static constructor
  alone. The type's own members can still call the private constructor, as `Create` must: that is
  the one trusted seam into a validated value object, stated on `IValidatedValue` and in SECURITY.md.
- **`Revalidate()` (decided 2026-09-28)** on a validated value object returns `Create(Value)`: today's
  rules applied to an instance that was rebuilt without them, by the EF satellite or
  `StoredJsonConverterFactory`. It is how an application finds the stored values a rule added later
  refuses, which the validation-free read path otherwise hides by design. It returns what `Create`
  returns, normalisation included, and a type that declares its own keeps it.
- **The companion class** holding `GetValue()`/`ValueOrNull()` sits at namespace level, as
  extension methods must, and is named after the whole nesting chain: `Order.Id` gets
  `OrderIdExtensions`. Named after the value object alone, `Order.Id` and `Customer.Id` both asked
  for `IdExtensions` and Metalama crashed (LAMA0001). A name that a declared type or another value
  object's companion already has is CMTK1007, naming it.
- **NodaTime.** Only `ValueObjectJsonAspect` handles NodaTime types, and it must not refer to
  NodaTime at compile time, which would force NodaTime on every consumer. It resolves the types
  with `TypeFactory.TryGetType` from the consumer's compilation, compares them by symbol, and uses
  NodaTime's converters (values and dictionary keys) only when
  NodaTime.Serialization.SystemTextJson is referenced too, and only where the options have no
  converter of their own for the type, which wins as it does for the wrapped type. Parsing reaches
  NodaTime types through their `[TypeConverter]` (`TypeDescriptor.RegisterType` and
  `GetConverterFromRegisteredType`, the trim-safe lookup). A type that `TypeDescriptor.GetConverter`
  described first, as MVC does binding a plain `LocalDate`, can no longer be registered, and then
  the converter that call cached is taken: the same one, measured under Native AOT by the smoke test.
- **Explicit `IParsable`.** `bool` and `char` implement their parsing interfaces explicitly, so the
  generated code calls the wrapped type's `TryParse` through `GeneratedParsing`, whose constrained
  type parameters reach an explicit implementation. `Parse` is `TryParse` and a throw (below).
- **Parsing culture (decided 2026-09-27).** In the generated `Parse`/`TryParse` a null
  `IFormatProvider` means the invariant culture, unlike the BCL, because the generated `ToString()`
  is invariant: what it writes must read back the same wherever the process runs, and with the
  current culture "1.5" was parsed as 15 in de-DE. A provider that is given is used as given. The
  type converter already did this. The text is the wrapped type's own default text, so it is as
  lossless as that, no more (measured 2026-10-13): `Parse(x.ToString(), null)` gives `x` back for the
  numbers, `Guid`, `DateOnly`, `TimeSpan`, `BigInteger`, `Version` and `Uri`, and not for `TimeOnly`
  (`03:04` for 03:04:05.123), `DateTime` (fraction and `Kind`) and `DateTimeOffset` (fraction), whose
  invariant short patterns drop them; the type converter and interpolation write the same text. The
  docs say so and point at `"O"` and the JSON form.
- **Formatting culture (decided 2026-09-27),** the counterpart: a null provider means the invariant
  culture in the generated `ToString(format, provider)`, both `TryFormat`s and `IConvertible`.
  Interpolation, `string.Format` and `Convert.ToString` pass null, and with the current culture
  `$"{amount}"` was "1,5" in de-DE while `amount.ToString()` was "1.5". Display text in a culture
  passes it: `string.Create(culture, $"{amount}")`.
- **The wrapped value's JSON contract** comes from `GeneratedJson.TypeInfo<T>(options, builtIn)`
  (decided 2026-09-27, [Native AOT](native-aot.md)): the options' own contract when their resolver has one, so a host's
  number handling and converters apply, and otherwise made in the serializer's order (a converter on
  the options, the type's `[JsonConverter]`, the built-in converter the aspect names by its property
  type). A source-generated context never sees what a value object wraps, and serializing through
  the options needs reflection. A dictionary key goes through the same contract's converter.
- **The bytes are the wrapped type's (decided 2026-09-28, [decision 26](adr/0026-value-object-s-json-wrapped-type.md)).** Every wrapped-type family
  writes and reads its values and keys through `GeneratedJsonPlan<T>`, the converter the options have
  for `T` or else the built-in (or NodaTime's) one: exactly what the serializer writes for a `T`, so a
  host's converter, number handling, enum converter and `DictionaryKeyPolicy` apply, and swapping a
  primitive for a value object changes no JSON. A `[JsonNumberHandling]` on the containing type or
  the property is the exception (measured 2026-10-13): the serializer applies it to the numbers it
  writes itself, the plan reads only the options' and the wrapped type's contract, and a converter
  cannot see its caller's attribute, so the docs say to set it on the options or the context. The
  earlier per-family readers and writers wrote
  `TimeOnly` as `"13:45:30.0000000"` and `DateTime` keys with seven fractional digits, ignored a
  host's converter for `Guid`, `DateTime` and integer keys, and skipped the key policy.
  `JsonParityTests` compares a value object with its wrapped type for 14 families under five option
  sets, as a value, a property and a key.
- **The wrapped value is written and read by its converter directly** (decided 2026-09-28), wherever
  that is what the serializer would do: a value converter, and, for a number type only, no number
  handling that changes the output (`WriteAsString`, named literals) or, for a string token, the
  input. Otherwise through the serializer, which alone applies number handling. A nested
  `JsonSerializer.Serialize`/`Deserialize` per value cost 35–40% (measured). The plan is kept per
  converter instance for the options it last saw; a static plan per wrapped type cost 8% on writes.
  The enum's built-in converter is made once per plan: made per call it allocated 4.7 KB per value.
- **A default instance over a reference type formats as `""`** (decided 2026-10-13). It is not supposed
  to exist (CMTK0001), but EF Core, MVC and serializers make one, and it holds null. Its `ToString()`
  over a string was already `""`; over a `Uri`, a `Version` or an `IPAddress` it, interpolation,
  `IFormattable`, both `TryFormat`s and the type converter threw `NullReferenceException`, as did
  `Convert.ToString` and `IConvertible` over a string. Every generated format answers `""` now (the
  span formats write nothing), and `IConvertible` over a reference type goes through `Convert` with the
  value as an object, which is the value's own conversion and, for null, Convert's: `""`, a number's
  default, `TypeCode.Empty` (`DefaultInstanceFormattingTests`). Harmless rather than loud, because a
  default instance already printed `""` over a string and the BCL prints a null that way.
- **Formatting and comparison never box** (decided 2026-09-28). The wrapped type's `ToString`,
  `TryFormat` and UTF-8 `TryFormat` are called through `GeneratedFormatting`, a constrained generic
  call that also reaches an explicit implementation, and comparison through `Comparer<T>.Default`,
  which the JIT specialises per type and, for an enum, compares the underlying values. Casting the
  value to the interface boxed it; an optimising JIT hid that for some types and not for others
  (enums), so the allocation tests run on a Debug build, where it never does.
- **Preserved references are refused (decided 2026-10-14, [decision 76](adr/0076-preserved-references-refused.md)).** The serializer
  keeps the references of one call, and the plan reaches a wrapped type the serializer walks through a
  nested call, which starts again: under `ReferenceHandler.Preserve` a value object over a class wrote
  `"$id":"1"` inside a document that had one, and two around one instance wrote it twice where the
  wrapped type writes a `$ref`, which the converter could not read (since 1.0.0). A converter cannot
  reach the caller's references, so `GeneratedJsonPlan` throws `NotSupportedException` under any
  handler but `IgnoreCycles` where the contract may hold references: an object or collection of a
  reference type, `object` under the serializer's own converter, or a struct with such a property it
  writes or reads, a nullable struct as the struct it holds, walked through the options' contracts
  ([decision 77](adr/0077-third-review-refines-74-to-76.md)). A property is judged by the converter in charge of
  it: one of one's own writes no `$id`, and one of the serializer's, a property's `CustomConverter`
  included, is judged by the serializer's contract of the type it converts, and refused where that
  contract has a converter of one's own (2026-10-14). A value converter writes no `$id`, so a struct of
  numbers and strings still writes the wrapped type's bytes. `IgnoreCycles` is left alone: a cycle through the value object fails at the depth limit, which
  is loud.
- **JSON reads never parse by rules of their own.** A value and a key are read by the plan's
  converter, which accepts exactly what the serializer accepts for the wrapped type. A `Parse` in
  the generated code once let `FormatException` escape, which a minimal API answers with 500 rather
  than 400, and read number keys with `NumberStyles.Any` ("1,000" as 1000).
- **What the wrapped type cannot read is refused without quoting it (decided 2026-09-28, decision
  27).** The BCL's and NodaTime's parse messages quote the input ("The input string 'SECRET' was not
  in a correct format."), and a malformed NodaTime JSON value carried it in an inner exception. A
  generated `Parse` is the wrapped type's `TryParse` and a `FormatException` naming the value object
  and the wrapped type; where there is no `TryParse` (a static `Parse`, a string constructor, a type
  converter) the exception is replaced, not wrapped; a JSON read throws `JsonException` the same way.
  An overflow is a `FormatException` too. `UnreadableInputTests` feeds a secret through every strategy
  and entry point and searches the whole exception chain. Out of reach: `JsonException.Path`, which
  the serializer fills with an unreadable dictionary key, and ASP.NET's own binding messages.
- **`TryParse` catches the wrapped type's refusal alone (decided 2026-10-13).** Over a type with no
  `TryParse` of its own (a type converter, a static `Parse`, a string constructor) the generated
  `TryParse` wrapped the inner parse and `Create` in a bare `catch`, so an exception `Create` threw was
  a `false` there, while `Parse`, and `TryParse` over an `int`, let it through. It catches only the
  `FormatException` the inner parse throws for unreadable text (`GeneratedParsing.Unreadable`), around
  the inner parse alone (`ThrowingCreateTests`).
- **`Uri` parses relative or absolute (decided 2026-09-28)**, as JSON and `UriTypeConverter` read it:
  `new Uri(s)` made "/orders/7" a `file://` URI on Unix and threw for "orders/7".
- **A hand-written `ToString()` is a seam (decided 2026-09-28, [decision 28](adr/0028-seams-declared-everything-else-refused.md)).** It was replaced
  silently, so a value object that hid a secret printed it. It is kept, and then no formatting
  interface (`IFormattable`, `ISpanFormattable`, `IUtf8SpanFormattable`) is generated, since
  interpolation would reach those first; the type converter and JSON still write the wrapped value.
- **More declarations refused (decided 2026-09-28).** Each failed as a Metalama error naming nothing
  the user wrote, or not at all:
  - CMTK1010: a value object declared in more than one `partial` part. Metalama 2026.1 (and 2027.0
    preview) writes the override of the record's synthesized `ToString` into every part (CS0111 as
    LAMA0611); no other advice overrides it (LAMA0041, LAMA0509, LAMA0500 measured). A part a source
    generator adds is not in `Sources` and does not count. The repro for an upstream report is in the
    phase 10 notes below.
  - CMTK1011: a hand-written member or attribute the generators introduce (`From`, `Value`,
    `Parse`/`TryParse`, `MinValue`/`MaxValue`, `[JsonConverter]`, `[TypeConverter]`, the interfaces),
    checked before anything is introduced, as CMTK1009 is; the seams (`TryFrom`, `FromKnownGood`,
    `Revalidate`, `CompareTo(TSelf)`, `ToString()`) stay allowed. Some of these were introduced with
    `OverrideStrategy.Ignore` and kept silently: a hand-written `Parse` became the binding entry
    point. `Create` implemented explicitly is CMTK1011 too, since the generated code calls
    `{Type}.Create`. Since phase 11 also a hand-written `Equals(TSelf)`/`GetHashCode()`/
    `IEquatable<TSelf>.Equals`, `PrintMembers` without `ToString()`, an explicit implementation of an
    interface the generators implement (CMTK1008 for the comparison interfaces), and a declared
    `IConvertible`.
  - `Create` is declared once, public: an explicit one beside it is CMTK1011, since a generic
    `T.Create` would reach a second rule set. A base record's members count as the value object's
    own: its `sealed` `ToString()` is the seam; an unsealed one, its `PrintMembers`, its explicit
    implementations of the generated interfaces and an `IConvertible` on it are CMTK1011, or CMTK1008
    for comparison. Members the compiler generated on a record from another assembly do not count.
  - **Equality over a base record is generated (decided 2026-10-14, [decision 75](adr/0075-equality-over-base-record-generated.md)).**
    A derived record's synthesized equality calls the base's, which compares the base's fields and
    whatever the base declares. A base comparing by reference made two value objects holding 42 unequal
    while `CompareTo` answered 0, and a base in another assembly holding a private identity, which a
    reference assembly does not carry and no check can see, put two of them in a `HashSet` and one in a
    `SortedSet`; both since 1.0.0. `ValueObjectContractAspect` introduces `Equals(TSelf)` and
    `GetHashCode()` over `_value` for a record class with a base; the record's own `Equals(object)`,
    `Equals(TBase)` and `==` call them. A base's hand-written `Equals(TBase)`, `GetHashCode()` or
    `IEquatable<TBase>.Equals` is then not called, which CMTK1023 says, a Warning since an Error that
    reports more waits for a major version (decision 34). A collection of the base calls the base's
    `IEquatable<TBase>`, which the value object implements again by listing `IReimplementedEquatable<TBase>`
    for every base: Metalama implements no interface a base already does, and C# implements one again
    for a class that lists an interface derived from it ([decision 77](adr/0077-third-review-refines-74-to-76.md)).
  - CMTK1012: state besides the wrapped value, an instance field, auto-property, `required` member or
    field-like event, declared or inherited. JSON, parsing and the materializer carried the wrapped
    value alone while equality compared the rest. Computed and static members stay allowed. In a
    referenced assembly a base's private fields are invisible, so a writable property counts there,
    and the generated equality over a base record keeps a private field out of it (decision 75).
  - CMTK1005 adds a generic wrapped type, tuples included (its equality is not value equality: a
    `List<T>` compares by reference), a value object named `Value` (CS0542), and a `file`-local one
    (LAMA0001, which failed the whole project).
- **Build failures that named nothing the user wrote (decided 2026-10-13, the pre-tag review).** Each
  was LAMA0611 or a bare compiler error:
  - A value object named as any other member the generators introduce (`From`, `TryFrom`,
    `FromKnownGood`, `Revalidate`, `Parse`, `TryParse`, `TryFormat`, `MinValue`, `MaxValue`,
    `CompareTo`, the private helpers) is CMTK1005, whatever it wraps, so the rule is one list
    (`ValueObjectDeclaration.GeneratedMemberNames`); a closed set named as one of its generated members
    (`Name`, `All`, `Match`, ...) is CMTK1021, a second definition of that id with the set's own message.
    Both were CS0542 inside the generated code.
  - `IValue<System.Enum>` is CMTK1005: `Enum.Parse<TEnum>(string)` was taken for a static `Parse` of its
    own (CS0411). The static-`Parse` strategy now takes only a non-generic one.
  - A wrapped type's static `Parse(string)` that returns another type, a string constructor or `Parse`
    that takes the text by `ref` or `out`, and an interface's `static virtual Parse` were taken for
    parsers, and the generated parsing failed (LAMA0611: CS0029, CS1620, CS8926; reported 2026-10-14,
    since 1.0.0). A parser is now what the generated call can call as it does and what gives the wrapped
    type back; anything else leaves the value object without parsing, as over a type with none. An
    abstract class's public string constructor is no parser either (CS0144, decision 77).
  - An `[Obsolete(error: true)]` on a value object, or on a type it is nested in, made the companion
    class's use of it CS0619. It is made to work, not refused: the companion class carries the nearest
    `[Obsolete]`, and C# reports no use of an obsolete type inside an obsolete declaration. Nothing else
    generated names the value object outside it. A closed set is generated inside itself and built.
  - A hand-written `operator ==`/`!=` between two of the type is CMTK1011 beside C#'s own CS0111, which
    names only `op_Equality`; it is a hand-written equality like `Equals(TSelf)`.
- **A `DateTime` is UTC on the wire (decided 2026-09-27).** Written and read, value and key, through
  `GeneratedJson.AsUtc`: a `Local` value converts, and an `Unspecified` one (an offset-less string, a
  column without a time zone) is taken as UTC with its digits kept. `ToUniversalTime()` read it as
  server-local, so 12:00 became 10:00Z on a server at +02:00 and 12:00Z on CI. The guards switch the
  process to a +05:30 zone, since CI runs in UTC.
- **`From(null)` throws** `ArgumentNullException` for a reference-type wrapped value, as
  `Option.Some(null)` does. It wrapped a null that `Value` promises it never holds.
- **Span parsing follows the wrapped type, not the strategy (2026-10-17).** `ISpanParsable` is
  generated wherever the wrapped type implements it (`SupportsSpan`), as `IUtf8SpanParsable` already
  was, and parses through the wrapped type's own span `TryParse`. The string strategy, picked before
  the span check, implemented `IParsable` alone, so a value object over `string` had no span parsing
  although `string` has it (the prober, claim C11). The string strategy still parses text as it is.
  Span parsing is not added over a type without it (`Uri`, enums, NodaTime): that would be a feature.
- **Enums** parse by name through `Enum.TryParse`, with a `FormatException` for an unknown name.
  `Convert.ChangeType` cannot make an enum from a string, so the former `IConvertible` strategy
  threw for every input while the type advertised `IParsable`. Nothing else that is convertible
  lacks `ISpanParsable`, so that strategy is gone.
- **String comparison is ordinal**, so ordering agrees with the record's equality. Culture
  comparison sorts "a" before "B" and ignores a zero-width space, and a `SortedSet` then dropped a
  value a `HashSet` kept.
- **A nullable wrapped type is CMTK1005.** The markers' `notnull` constraint is only a warning
  (CS8714), and `IValue<int?>` then failed inside the generated code as LAMA0611/0612.
- **`object` as the wrapped type is CMTK1005 (decided 2026-10-13).** The serializer writes an `object`
  by the run-time type of what it holds; the plan calls the `object` converter directly, which does
  not, so `OverObject.From(5)` was written as `{}` where an `object` property holding 5 is written as
  5. Measured beside it: an interface (`IDisposable`, a `[JsonPolymorphic]` one) and an abstract class
  (a plain one, an abstract record, a `[JsonPolymorphic]` one), and `ValueType` and `Array`, are written and
  read exactly as the serializer writes and reads the declared type, refusals included, so they stay
  allowed.
- **A value object never wraps a value object (decided 2026-09-27).** CMTK1005, answered in the
  fabric because it reads the wrapped types (see "An aspect never scans its namespace" below).
  - **A cycle** (`sealed partial record SelfWrap : IValue<SelfWrap>`, or A wraps B wraps A) has no
    finite form: the fallback JSON converter serializes the wrapped value through the options,
    which is its own converter again, and a schema walk never ends. As a record class it compiled
    without a word (spikes/OpenApiSchemas finding 16), as a struct it failed inside the generated
    code (CS0523 as LAMA0611). The message names the chain: `it wraps itself (A -> B -> A)`.
  - **Any other value object** (`Outer : IValue<OrderId>`) terminates, but its surface depended on
    where the inner one was declared. Over one from a referenced project it got every interface;
    over one from its own project it silently lacked `IParsable`, `IComparable`, `ISpanFormattable`,
    `IConvertible`, `IMinMaxValue` and the type converter, and sorting it threw: the aspects of one
    layer do not see what their sibling instances introduce (measured 2026-09-27). The message
    names what to wrap instead (`wrap 'Guid' instead`); a validated value object applies the inner
    one's rules in its `Create`. Refused rather than made to work: a surface that follows the
    project layout is the silent failure this repo exists to prevent, and lifting a refusal later
    breaks nobody, while adding one after 1.0.0 would.
  - **A closed set as the wrapped type is not refused** (decided 2026-10-13,
    [decision 60](adr/0060-value-object-over-closed-set-generated-in-full.md)), wherever it is declared.
    Over a set of its own project `OverSet : IValue<SetA>` lacked `Parse`, `TryParse`, `IParsable` and
    its own type converter while its JSON worked, because `ClosedSetAspect` ran last, after the
    value-object aspects. It is a different aspect class, so ordering fixes what ordering cannot for a
    value object over a value object: `ClosedSetAspect` runs first, and the value-object aspects see the
    set's `IParsable`, `Parse` and `[TypeConverter]` as they see a referenced set's.
  - The satellites keep their own refusal ([OpenAPI](openapi.md)): a value object declared by hand, with the
    interfaces and `GeneratedValueObjectAttribute<,>` the satellites read ([Discovery](discovery.md)), can still wrap itself.
- **Every JSON key goes through the wrapped type's own converter**, which knows the type's key
  format where it has one (an enum by name, a `Uri` as its text) and throws `NotSupportedException`
  where it has none. Writing the serialized value as the property name gave a `Uri` key quotes inside
  its quotes and an enum key its number, and neither read back.
- **A record class sorts null first.** Its generated `CompareTo` and comparison operators take a
  nullable operand, as `IComparable<T>.CompareTo(T?)` and the record's own `==` do, and treat null
  as smallest, where they threw `NullReferenceException`.
- **Ordering follows the wrapped type.** `CompareTo` delegates to the wrapped type's, ordinal for a
  string, so it agrees with equality exactly where the wrapped type's own does. A custom wrapped
  type whose `CompareTo` returns 0 for values its `Equals` tells apart makes a `SortedSet` drop one,
  of the value object and of the wrapped type alike (measured 2026-09-27). That is the wrapped
  type's contract to keep, and the marker interfaces' documentation says so.
- **One comparison seam (decided 2026-09-27).** A hand-written `CompareTo(TSelf)` is kept, and the
  object overload, the operators and the interfaces are derived from it, as `TryFrom` is for the
  factories. Any other hand-written comparison member is CMTK1008: an operator failed the aspect
  (LAMA0500, naming no fix), and an object overload was kept silently beside a generated generic
  one that need not agree with it.
- **An aspect never scans its namespace (decided 2026-09-27,
  [spikes/ConcurrentNamespaceTypes](../spikes/ConcurrentNamespaceTypes/README.md)).** What owns
  a companion name is answered in the fabric, before any introduction, and reaches the aspect
  through its constructor and the aspect state. Anything that has to look beyond the aspect's own
  target goes the same way.
- **Never look up the code model by name (decided 2026-09-28, same spike).** Compile-time code
  finds a type or member by enumerating a collection and comparing names: no `OfName`, no
  `OfExactSignature`/`OfCompatibleSignature`, no string indexer on `Fields`/`Properties`/`Events`.
  Metalama builds a collection's by-name index lazily and without a lock, and runs the fabric's
  factory, like the instances of one aspect layer, in parallel on one code model. An `OfName` that
  overlapped another caller completing the collection returned nothing for a declared
  `TakenNameExtensions`, and the build compiled a second one (CS0260) or Metalama failed the aspect
  (LAMA0531). In the fabric that missed CMTK1007 in 13 of 300 builds; the move there on 2026-09-27
  had measured 0 in 48 by chance and blamed sibling introductions, which was wrong. With concurrent
  build off: 0 in 200. Enumerating, which takes the collection's lock: 0 in 600, and 4 in 150
  again with it reverted. The race has no deterministic test, so `CodeModelLookupTests`
  (conventions) fails on any such lookup in `.Generators`.
- **Metalama 2026.1.** Aspect state uses `IDurableRef`, which exists in 2026.1. `[Durable]` on the
  `_value` template placeholder is 2027.0-only and stays out until the upgrade ([decision 4](adr/0004-metalama-2026-1-x-latest-stable.md)). Build
  each aspect on 2026.1 as it lands, and use no 2027.0-only API.
- **`AspectOrder`** lives in `CodoMetis.TypeKit.Generators`: `ClosedSetAspect` first, then
  `ValueObjectAspect`, which leaves the state the other value-object aspects read
  ([decision 60](adr/0060-value-object-over-closed-set-generated-in-full.md)). The closed-set aspect reads
  nothing the value-object aspects introduce: the set's own fields, constructors, initializer text,
  methods, attributes and interfaces, none of which they touch, and no member's data type (checked
  2026-10-13). Open point: ordering against a consumer's own aspects (fabric spike D).
- **Not in scope:** a `Try`/`Unit` type ([Option and Result](option-result.md) non-goals; an exception enters a result through
  `Result.Catch`, phase 13), clock-type analyzers, and decimal JSON converters (no aspect needs them,
  checked in phase 3). The text guards joined phase 13 on 2026-10-13, as string extensions in the
  base package.
- **Public aspects (decided 2026-10-13, [decision 43](adr/0043-exceptions-type-argument-constraints-constructor-guarantee.md)).** The value-object aspects are internal: the
  fabric applies them, and their surface is what they generate. An aspect a consumer applies by hand
  may be public when the aspect is the feature, with the attribute and its diagnostics as its
  surface. None ships yet: the first candidate, a parameterless-constructor guarantee, went to the
  analyzer and a code fix (phase 13), because its value is the error, which has to appear in a
  project that never compiles with Metalama.
