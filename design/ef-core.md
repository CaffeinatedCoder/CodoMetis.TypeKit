# EF Core

- **Materializer.** An interface in `CodoMetis.TypeKit.ValueObjects`:
  `IValueObjectMaterializer<TSelf,T> { static abstract TSelf Materialize(T value); }`. The aspect
  implements it **explicitly**, so it is invisible on the type's public surface and reachable only
  through a constrained generic. Documented contract: *skips validation; for values this
  application wrote itself; never call it on input.* CMTK0004 enforces who may call it.
- **One pair of conversions.** `ValueObjectConverter<TVO,T> : ValueConverter<TVO,T>`, public so a
  property can also name it explicitly. Its expressions call its own public static
  `ProviderValue` and `Materialize`: an expression tree cannot call a static abstract member
  directly (CS8927), and EF's compiled model writes the expressions out as C# in the application's
  assembly, where an internal helper did not compile ([§11](native-aot.md)). `UseTypeKit()` composes a plain
  `ValueConverter<TVO,T>` over the same expressions, the type the compiled model recreates, because a
  precompiled query casts a property's converter to its design-time type.
- **No per-type comparer.** A `readonly record struct` already has value equality, and EF's default
  comparer uses it. A test pins that it agrees with comparing `.Value`.
- **Application (decided 2026-09-27, spikes/EfMapping).** `optionsBuilder.UseTypeKit()` registers
  an options extension. The name is deliberately not value-object specific, so a later `Option<T>`
  column mapping can join it. It adds an `IRelationalTypeMappingSourcePlugin` that answers any
  `IValueObject<TSelf, T>` with the provider's mapping for `T` and the converter composed onto it.
  EF consults it per CLR type wherever it maps one: property discovery, keys, foreign keys,
  primitive-collection elements, query parameters. The lookup's facets are passed on. The plugin
  composes the JSON reader/writer itself, with both types known through the attribute's visitor:
  EF's own composition uses `MakeGenericType`, which it refuses under Native AOT, where a precompiled
  query still maps a value-object parameter at run time.
  - Not a replaced `IValueConverterSelector`: it maps the same, but a second library replacing the
    selector (as strongly-typed-id guides recommend) would take it away, and the resulting model
    error does not name the cause. Plugins are additive. This was first decided the other way on
    an unmeasured assumption, and reversed by the spike the same day.
  - Not a pre-convention: `Properties<T>()` needs the list of types up front, which means a scan.
  - An application that builds EF's internal service provider calls
    `AddEntityFrameworkTypeKit()` on that service collection instead.
- **Translators.** `ValueObjectMemberTranslatorPlugin` / `ValueObjectMethodCallTranslatorPlugin`
  are registered by the same options extension, so a consumer wires nothing else. A column
  operand is **re-typed** as the wrapped type, keeping the column's store type, rather than cast:
  `Convert` produced `o."Code"::text = 'ABC'`, which PostgreSQL discards but which is not a no-op
  everywhere (on SQL Server, a cast to `nvarchar(max)` can stop an index seek). Any other operand
  is still converted. The method translator acts only on a method whose signature is the unwrap,
  from a value object (or its `Nullable`) to what it wraps: the attribute is public, and a
  hand-marked `Length(this ProbeCode)` was translated as the column (`o."Code" = '3'`). Any other
  method carrying it is left to EF, which refuses the call.
  - **The element of a primitive collection is converted, not re-typed (decided 2026-09-28).** EF
    infers a collection's element mapping from the columns that read its table expression (`unnest`,
    `json_each`, `OPENJSON`, `VALUES`), and a re-typed element column handed it the wrapped type's
    mapping for a collection of value objects: `o.Tags.Any(t => t.Value == "x")` and
    `ids.Any(i => i.Value == o.Id.Value)` threw during translation on PostgreSQL and SQLite. EF 10's
    `ColumnExpression` no longer knows its table, so the element column is recognised by the name
    every relational provider gives it, `value`; the name only chooses between two correct
    translations, and a table column named `Value` gets the cast (`t.value::text = 'x'`).
- **Keys over an integer are generated on add (decided 2026-09-28, [decision 23](adr/0023-value-object-key-over-integer-generated.md)).** EF's value
  generation convention reads the property's CLR type, so a value-object key was never generated: an
  entity added without one was stored as 0, and switching an `int` key to a value object dropped its
  identity column. A convention plugin (first among the model-finalizing conventions, so a provider's
  identity strategy sees it) marks a single-column, non-foreign key over `int`, `long` or `short` as
  generated on add, at convention precedence. A key over a `Guid` is left alone: the application
  assigns `OrderId.New()`, and a generated key that is already set makes EF take a new entity reached
  through a navigation for an existing one.
- **Conventions for the wrapped type do not reach a value object.** `ConfigureConventions`
  (`Properties<decimal>()`) matches the property's CLR type; the README says to configure the value
  object (`Properties<Amount>()`). Measured 2026-09-28 for precision, maximum length, column type and
  an enum conversion.
- **Stored JSON (decided 2026-09-27).** The generated JSON converter applies `Create` ([§5](aspects.md)), which
  is right for input and wrong for JSON the application stored itself (an event store, a document
  column): a rule added later would make old documents unreadable. `StoredJsonConverterFactory`, in
  the base package because it is a System.Text.Json concern, is the opt-in for such a store's
  `JsonSerializerOptions`. It hands out the generated converter in a mode only it can create
  (a private constructor), so stored documents are read in exactly the format they were written
  in, dictionary keys included. SECURITY.md lists it beside the EF satellite as the two
  validation-free paths.
- **How far the translation is unique, measured 2026-09-27**
  ([spikes/ValueTranslation](../spikes/ValueTranslation/README.md)):
  - `.Value` in a query predicate fails in Vogen 8.0.7 and in plain EF Core 10. Thinktecture
    10.5.0 has no public `.Value`.
  - Querying the underlying value through a **conversion-operator cast** works in plain EF Core
    10, and that is how Thinktecture users get it.
  - README claim: "`.Value` works in LINQ as it does in memory". Never "the only library that can
    query by the underlying value".
