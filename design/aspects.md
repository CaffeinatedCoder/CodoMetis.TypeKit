# Aspects

- **Aspect-class count.** Ten classes. The Metalama Open Source edition (MIT) has no aspect-class
  limit (pricing page, 2026-09-26). The unlicensed CI build confirms it (phase 6).
- **Entry points.** `ValueObjectAspect` introduces three private helpers on every value
  object, and they are the only way the JSON, parsing and type-converter aspects create an
  instance: `__FromJson` (a refusal throws `JsonException`), `__FromText` (`FormatException`) and
  `__TryFromText` (`false`). A plain value object constructs directly; a validated one calls
  `Create` through `GeneratedFactories` in the base package. The exception is the explicit
  `IValueObjectMaterializer.Materialize`, by contract. `MinValue`/`MaxValue` are only generated for
  a plain value object.
- **Declarations that cannot be generated** are errors, so no type is left half-generated:
  CMTK1000/1001/1002 (not `partial`, not a record, a struct not `readonly`), CMTK1003 (more than one
  marker), CMTK1004 (a validated marker whose first type argument is another type), CMTK1005 (a
  generic value object, one that derives from another value object, a wrapped type that is not
  a class, struct or enum, or one that is a value object: itself, one that reaches it again, or
  any other, see below), CMTK1006 (a record class that is not `sealed`: a derived record compares
  equal only to its own type, which is not value equality), CMTK1007 (the name of the
  `GetValue`/`ValueOrNull` class is taken, see below), CMTK1008 (a comparison member beside the
  seam, see below), CMTK1009 (a hand-written constructor, see below).
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
  `GetConverterFromRegisteredType`, the trim-safe lookup).
- **Explicit `IParsable`.** `bool` and `char` implement their parsing interfaces explicitly, so the
  generated code calls the wrapped type's `TryParse` through `GeneratedParsing`, whose constrained
  type parameters reach an explicit implementation. `Parse` is `TryParse` and a throw (below).
- **Parsing culture (decided 2026-09-27).** In the generated `Parse`/`TryParse` a null
  `IFormatProvider` means the invariant culture, unlike the BCL, because the generated `ToString()`
  is invariant: `Parse(x.ToString(), null)` must round-trip, and with the current culture it parsed
  "1.5" as 15 in de-DE. A provider that is given is used as given. The type converter already did
  this.
- **Formatting culture (decided 2026-09-27),** the counterpart: a null provider means the invariant
  culture in the generated `ToString(format, provider)`, both `TryFormat`s and `IConvertible`.
  Interpolation, `string.Format` and `Convert.ToString` pass null, and with the current culture
  `$"{amount}"` was "1,5" in de-DE while `amount.ToString()` was "1.5". Display text in a culture
  passes it: `string.Create(culture, $"{amount}")`.
- **The wrapped value's JSON contract** comes from `GeneratedJson.TypeInfo<T>(options, builtIn)`
  (decided 2026-09-27, [§11](native-aot.md)): the options' own contract when their resolver has one, so a host's
  number handling and converters apply, and otherwise made in the serializer's order (a converter on
  the options, the type's `[JsonConverter]`, the built-in converter the aspect names by its property
  type). A source-generated context never sees what a value object wraps, and serializing through
  the options needs reflection. A dictionary key goes through the same contract's converter.
- **The bytes are the wrapped type's (decided 2026-09-28, [decision 26](adr/0026-value-object-s-json-wrapped-type.md)).** Every wrapped-type family
  writes and reads its values and keys through `GeneratedJsonPlan<T>`, the converter the options have
  for `T` or else the built-in (or NodaTime's) one: exactly what the serializer writes for a `T`, so a
  host's converter, number handling, enum converter and `DictionaryKeyPolicy` apply, and swapping a
  primitive for a value object changes no JSON. The earlier per-family readers and writers wrote
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
- **Formatting and comparison never box** (decided 2026-09-28). The wrapped type's `ToString`,
  `TryFormat` and UTF-8 `TryFormat` are called through `GeneratedFormatting`, a constrained generic
  call that also reaches an explicit implementation, and comparison through `Comparer<T>.Default`,
  which the JIT specialises per type and, for an enum, compares the underlying values. Casting the
  value to the interface boxed it; an optimising JIT hid that for some types and not for others
  (enums), so the allocation tests run on a Debug build, where it never does.
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
  - CMTK1012: state besides the wrapped value, an instance field, auto-property, `required` member or
    field-like event, declared or inherited. JSON, parsing and the materializer carried the wrapped
    value alone while equality compared the rest. Computed and static members stay allowed. In a
    referenced assembly a base's private fields are invisible, so a writable property counts there.
  - CMTK1005 adds a generic wrapped type, tuples included (its equality is not value equality: a
    `List<T>` compares by reference), a value object named `Value` (CS0542), and a `file`-local one
    (LAMA0001, which failed the whole project).
- **A `DateTime` is UTC on the wire (decided 2026-09-27).** Written and read, value and key, through
  `GeneratedJson.AsUtc`: a `Local` value converts, and an `Unspecified` one (an offset-less string, a
  column without a time zone) is taken as UTC with its digits kept. `ToUniversalTime()` read it as
  server-local, so 12:00 became 10:00Z on a server at +02:00 and 12:00Z on CI. The guards switch the
  process to a +05:30 zone, since CI runs in UTC.
- **`From(null)` throws** `ArgumentNullException` for a reference-type wrapped value, as
  `Option.Some(null)` does. It wrapped a null that `Value` promises it never holds.
- **Enums** parse by name through `Enum.TryParse`, with a `FormatException` for an unknown name.
  `Convert.ChangeType` cannot make an enum from a string, so the former `IConvertible` strategy
  threw for every input while the type advertised `IParsable`. Nothing else that is convertible
  lacks `ISpanParsable`, so that strategy is gone.
- **String comparison is ordinal**, so ordering agrees with the record's equality. Culture
  comparison sorts "a" before "B" and ignores a zero-width space, and a `SortedSet` then dropped a
  value a `HashSet` kept.
- **A nullable wrapped type is CMTK1005.** The markers' `notnull` constraint is only a warning
  (CS8714), and `IValue<int?>` then failed inside the generated code as LAMA0611/0612.
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
  - The satellites keep their own refusal ([§7](openapi.md)): a value object declared by hand, with the
    interfaces and `GeneratedValueObjectAttribute<,>` the satellites read ([§3](discovery.md)), can still wrap itself.
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
- **`AspectOrder`** lives in `CodoMetis.TypeKit.Generators`. Open point: ordering against a
  consumer's own aspects (fabric spike D).
- **Not in scope:** a `Try`/`Unit` type ([§9](option-result.md) non-goals; an exception enters a result through
  `Result.Catch`, phase 13), clock-type analyzers, and decimal JSON converters (no aspect needs them,
  checked in phase 3). The text guards joined phase 13 on 2026-10-13, as string extensions in the
  base package.
- **Public aspects (decided 2026-10-13, [decision 43](adr/0043-exceptions-type-argument-constraints-constructor-guarantee.md)).** The value-object aspects are internal: the
  fabric applies them, and their surface is what they generate. An aspect a consumer applies by hand
  may be public when the aspect is the feature, with the attribute and its diagnostics as its
  surface. None ships yet: the first candidate, a parameterless-constructor guarantee, went to the
  analyzer and a code fix (phase 13), because its value is the error, which has to appear in a
  project that never compiles with Metalama.
