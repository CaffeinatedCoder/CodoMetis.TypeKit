using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// What a consumer's build reports for declarations the generators cannot generate, and that the
/// analyzer works beside the real generators assembly.
/// </summary>
/// <remarks>
/// <para>
/// These outcomes are build errors, so no test in a compiling project can observe them. The fixture
/// builds a throwaway consumer that references the generators and the analyzer as a package consumer
/// would get them, and every test reads its diagnostics.
/// </para>
/// <para>
/// CMTK0002 looks for the generators by assembly identity. Its unit tests use a stand-in assembly;
/// this is where the real one has to keep it silent.
/// </para>
/// </remarks>
public sealed partial class BuildOutcomeTests(BuildOutcomeTests.Consumer consumer) : IClassFixture<BuildOutcomeTests.Consumer>
{
    [Theory]
    [InlineData("CMTK1000", "NotPartial")]
    [InlineData("CMTK1001", "NotRecord")]
    [InlineData("CMTK1002", "NotReadonly")]
    [InlineData("CMTK1003", "TwoMarkers")]
    [InlineData("CMTK1004", "Misnamed")]
    [InlineData("CMTK1005", "Generic")]
    [InlineData("CMTK1005", "Outer<T>.Middle.DeeplyNested")]
    [InlineData("CMTK1005", "ArrayBacked")]
    [InlineData("CMTK1005", "NullableBacked")]
    [InlineData("CMTK1005", "DerivesFromAValueObject")]
    [InlineData("CMTK1005", "WrapsItself")]
    [InlineData("CMTK1005", "CycleOne")]
    [InlineData("CMTK1005", "CycleTwo")]
    [InlineData("CMTK1005", "StructCycleOne")]
    [InlineData("CMTK1005", "StructCycleTwo")]
    [InlineData("CMTK1005", "WrapsAValueObject")]
    [InlineData("CMTK1005", "ListBacked")]
    [InlineData("CMTK1005", "ImmutableArrayBacked")]
    [InlineData("CMTK1005", "PairBacked")]
    [InlineData("CMTK1005", "TupleBacked")]
    [InlineData("CMTK1005", "NamedTupleBacked")]
    [InlineData("CMTK1005", "Named.Value")]
    [InlineData("CMTK1005", "FileLocal")]
    [InlineData("CMTK1005", "FileLocalBelowRegion")]
    [InlineData("CMTK1005", "FileLocalBelowPragma")]
    [InlineData("CMTK1005", "FileLocalBelowDisabledText")]
    [InlineData("CMTK1005", "FileLocalAfterAttribute")]
    [InlineData("CMTK1005", "FileLocalFixtures.NestedInFileLocal")]
    [InlineData("CMTK1006", "NotSealed")]
    [InlineData("CMTK1007", "TakenName")]
    [InlineData("CMTK1007", "ShopId")]
    [InlineData("CMTK1007", "Shop.Id")]
    [InlineData("CMTK1008", "WithOperator")]
    [InlineData("CMTK1008", "WithObjectCompareTo")]
    [InlineData("CMTK1008", "ExplicitComparable")]
    [InlineData("CMTK1008", "ExplicitObjectComparable")]
    [InlineData("CMTK1008", "ExplicitComparisonOperators")]
    [InlineData("CMTK1009", "CtorSameSignature")]
    [InlineData("CMTK1009", "CtorBypass")]
    [InlineData("CMTK1009", "CtorUnassignedStruct")]
    [InlineData("CMTK1009", "CtorUnassignedClass")]
    [InlineData("CMTK1009", "CtorParameterless")]
    [InlineData("CMTK1009", "CtorCopy")]
    [InlineData("CMTK1009", "PositionalStruct")]
    [InlineData("CMTK1009", "PositionalClass")]
    [InlineData("CMTK1010", "SplitInOneFile")]
    [InlineData("CMTK1010", "SplitAcrossFiles")]
    [InlineData("CMTK1011", "HandWrittenFrom")]
    [InlineData("CMTK1011", "HandWrittenValue")]
    [InlineData("CMTK1011", "HandWrittenParse")]
    [InlineData("CMTK1011", "HandWrittenFormat")]
    [InlineData("CMTK1011", "HandWrittenMinValue")]
    [InlineData("CMTK1011", "HandWrittenJsonConverter")]
    [InlineData("CMTK1011", "HandWrittenTypeConverter")]
    [InlineData("CMTK1011", "HandWrittenInterface")]
    [InlineData("CMTK1011", "ExplicitCreate")]
    [InlineData("CMTK1011", "ExplicitCreateBesidePublic")]
    [InlineData("CMTK1011", "CaseInsensitiveEquals")]
    [InlineData("CMTK1011", "HashCodeOnly")]
    [InlineData("CMTK1011", "ExplicitEquatable")]
    [InlineData("CMTK1011", "PinStruct")]
    [InlineData("CMTK1011", "PinClass")]
    [InlineData("CMTK1011", "ExplicitParsable")]
    [InlineData("CMTK1011", "ExplicitFormattable")]
    [InlineData("CMTK1011", "ExplicitSpanFormattable")]
    [InlineData("CMTK1011", "ExplicitMinMax")]
    [InlineData("CMTK1011", "ExplicitConvertible")]
    [InlineData("CMTK1011", "ExplicitEqualityOperators")]
    [InlineData("CMTK1012", "WithAutoProperty")]
    [InlineData("CMTK1012", "WithCacheField")]
    [InlineData("CMTK1012", "WithRequiredMember")]
    [InlineData("CMTK1012", "WithInheritedState")]
    [InlineData("CMTK1012", "WithEvent")]
    public void A_declaration_that_cannot_be_generated_is_an_error(string id, string type) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains($"'{type}"), $"{id} on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// Every value object over another one is refused, so a cycle would be refused without this
    /// reason too, but not told that it loops, or through which types.
    /// </summary>
    [Theory]
    [InlineData("WrapsItself", "it wraps itself (WrapsItself -> WrapsItself)")]
    [InlineData("CycleOne", "it wraps itself (CycleOne -> CycleTwo -> CycleOne)")]
    [InlineData("StructCycleTwo", "it wraps itself (StructCycleTwo -> StructCycleOne -> StructCycleTwo)")]
    [InlineData("WrapsAValueObject", "it wraps 'Fine', which is a value object itself; wrap 'int' instead")]
    public void A_value_object_over_a_value_object_is_refused_with_what_it_reaches(string type, string reason) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1005" && error.Message.StartsWith($"'{type}'") && error.Message.Contains(reason), $"CMTK1005 on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// A generic wrapped type failed inside the generated code (LAMA0611). The refusal says why, since
    /// the type is often a perfectly good type: its equality is not guaranteed to be value equality.
    /// </summary>
    [Theory]
    [InlineData("ListBacked", "the wrapped type 'List<string>' is generic")]
    [InlineData("NamedTupleBacked", "the wrapped type '(int X, int Y)' is generic")]
    public void A_generic_wrapped_type_is_refused_with_the_reason(string type, string reason) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1005" && error.Message.StartsWith($"'{type}'") && error.Message.Contains(reason) && error.Message.Contains("compares by reference"), consumer.Output);

    /// <summary>
    /// Metalama writes its override of the record's <c>ToString()</c> into every part of a declaration,
    /// and the second copy failed inside the generated code (LAMA0611, CS0111).
    /// </summary>
    [Fact]
    public void A_value_object_in_several_parts_is_refused_with_the_count() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1010" && error.Message.StartsWith("'SplitAcrossFiles' is declared in 3 parts"), consumer.Output);

    /// <summary>
    /// A part that a source generator adds is not the user's, and Metalama does not write into it: a
    /// validated value object with a <c>[GeneratedRegex]</c> member builds, and must keep building.
    /// A guard against over-correction, since it built before CMTK1010 too.
    /// </summary>
    [Fact]
    public void A_part_a_source_generator_adds_does_not_count() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'Sku'"), consumer.Output);

    /// <summary>
    /// A hand-written member that a generator introduces failed the aspect or the generated code
    /// (LAMA0500, LAMA0503, LAMA0512, LAMA0521, LAMA0611) naming no fix. The error names each one.
    /// </summary>
    [Theory]
    [InlineData("HandWrittenFrom", "HandWrittenFrom.From(string)")]
    [InlineData("HandWrittenValue", "HandWrittenValue.Value")]
    [InlineData("HandWrittenParse", "HandWrittenParse.Parse(string, IFormatProvider?)")]
    [InlineData("HandWrittenFormat", "HandWrittenFormat.ToString(string?, IFormatProvider?)")]
    [InlineData("HandWrittenMinValue", "HandWrittenMinValue.MinValue")]
    [InlineData("HandWrittenJsonConverter", "the attribute JsonConverter")]
    [InlineData("HandWrittenTypeConverter", "the attribute TypeConverter")]
    [InlineData("HandWrittenInterface", "the interface IValueObject<HandWrittenInterface, int>")]
    [InlineData("ExplicitCreate", "Create only as an explicit implementation of IValidatedValue<ExplicitCreate, string, Fault>")]
    [InlineData("ExplicitCreateBesidePublic", "Create both as a static method and as an explicit implementation of IValidatedValue<ExplicitCreateBesidePublic, string, Fault>, so it is not generated: every generated way in calls ExplicitCreateBesidePublic.Create and generic code calling T.Create reaches the explicit one")]
    public void A_hand_written_generated_member_is_refused_by_name(string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(member), consumer.Output);

    /// <summary>
    /// A hand-written equality was kept while the generated ordering, the JSON keys and the column went
    /// on comparing the wrapped value, so values it called equal sorted apart. The error names each
    /// member and points at normalising in <c>Create</c>, which a plain value object gets by becoming a
    /// validated one.
    /// </summary>
    [Theory]
    [InlineData("CaseInsensitiveEquals", "CaseInsensitiveEquals.Equals(CaseInsensitiveEquals), CaseInsensitiveEquals.GetHashCode(), so", "(declare CaseInsensitiveEquals as IValidatedValue to get one)")]
    [InlineData("HashCodeOnly", "HashCodeOnly.GetHashCode(), so", "normalise them in Create, so")]
    [InlineData("ExplicitEquatable", "the explicit implementation of IEquatable<ExplicitEquatable>.Equals(ExplicitEquatable), so", "(declare ExplicitEquatable as IValidatedValue to get one)")]
    public void A_hand_written_equality_is_refused_by_name(string type, string members, string remedy) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(members)
                                            && error.Message.Contains("a value object's equality is its wrapped value's") && error.Message.Contains(remedy), consumer.Output);

    /// <summary>
    /// An explicit implementation of an interface the generators implement was kept beside the generated
    /// public member, and every caller through the interface reached it: a generic <c>T.Parse</c> bypassed
    /// <c>Create</c>, <c>Comparer&lt;T&gt;.Default</c> disagreed with <c>&lt;</c>, and interpolation printed
    /// what <c>ToString()</c> did not. The error names the interface member.
    /// </summary>
    [Theory]
    [InlineData("CMTK1011", "ExplicitParsable", "the explicit implementation of IParsable<ExplicitParsable>.Parse(string, IFormatProvider?), the explicit implementation of IParsable<ExplicitParsable>.TryParse(string?, IFormatProvider?, out ExplicitParsable)")]
    [InlineData("CMTK1011", "ExplicitFormattable", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?)")]
    [InlineData("CMTK1011", "ExplicitSpanFormattable", "the explicit implementation of ISpanFormattable.TryFormat(")]
    [InlineData("CMTK1011", "ExplicitMinMax", "the explicit implementation of IMinMaxValue<ExplicitMinMax>.MinValue")]
    [InlineData("CMTK1011", "ExplicitConvertible", "the interface IConvertible")]
    [InlineData("CMTK1011", "ExplicitEqualityOperators", "the explicit implementation of IEqualityOperators<ExplicitEqualityOperators, ExplicitEqualityOperators, bool>.operator ==(")]
    [InlineData("CMTK1008", "ExplicitComparable", "the explicit implementation of IComparable<ExplicitComparable>.CompareTo(ExplicitComparable)")]
    [InlineData("CMTK1008", "ExplicitObjectComparable", "the explicit implementation of IComparable.CompareTo(object?)")]
    [InlineData("CMTK1008", "ExplicitComparisonOperators", "the explicit implementation of IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>.operator <(")]
    public void An_explicit_implementation_of_a_generated_interface_is_refused_by_name(string id, string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(member), consumer.Output);

    /// <summary>
    /// A hand-written <c>PrintMembers</c> was replaced silently with the record's <c>ToString()</c>, which
    /// calls it, so what it hid was printed. The error points at <c>ToString()</c>, the seam.
    /// </summary>
    [Theory]
    [InlineData("PinStruct")]
    [InlineData("PinClass")]
    public void A_hand_written_PrintMembers_is_refused_with_the_seam(string type) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares {type}.PrintMembers(StringBuilder), so it is not generated")
                                            && error.Message.Contains("declare ToString() instead"), consumer.Output);

    /// <summary>
    /// Instance state besides the wrapped value was generated without a word: JSON, parsing and the
    /// materializer carried the wrapped value alone while the record's equality compared the rest, so a
    /// <c>Currency</c> was lost on a round trip and a lazily filled cache made equal instances unequal.
    /// A <c>required</c> member failed inside the generated code (LAMA0611, CS9035). The error names
    /// each member, an inherited one too.
    /// </summary>
    [Theory]
    [InlineData("WithAutoProperty", "the auto-property WithAutoProperty.Currency besides its wrapped value")]
    [InlineData("WithCacheField", "the field WithCacheField._domain besides its wrapped value")]
    [InlineData("WithRequiredMember", "the required property WithRequiredMember.Currency besides its wrapped value")]
    [InlineData("WithInheritedState", "the auto-property Audited.At (inherited) besides its wrapped value")]
    [InlineData("WithEvent", "the event WithEvent.Changed besides its wrapped value")]
    public void Instance_state_besides_the_wrapped_value_is_refused_by_name(string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1012" && error.Message.StartsWith($"'{type}' holds {member}, so it is not generated"), consumer.Output);

    /// <summary>
    /// The seams stay allowed: <c>Create</c>, <c>TryFrom</c>, <c>FromKnownGood</c>, <c>Revalidate</c>,
    /// <c>CompareTo(TSelf)</c> and <c>ToString()</c>. A <c>MinValue</c> of another type, or one the value
    /// object cannot reach, is not the wrapped type's bound: no <c>MinValue</c> is generated, where it
    /// failed inside the generated code (LAMA0611, CS1503 and CS0122).
    /// </summary>
    [Theory]
    [InlineData("WithEverySeam", "'WithEverySeam")]
    [InlineData("Share", "Percent")]
    [InlineData("Rank", "Level.M")]
    [InlineData("WithComputedMembers", "'WithComputedMembers")]
    [InlineData("PinWithToString", "'PinWithToString")]
    [InlineData("ExplicitFormattableWithToString", "'ExplicitFormattableWithToString")]
    [InlineData("ExplicitComparableUri", "'ExplicitComparableUri")]
    [InlineData("WithStatelessBase", "Described.")]
    public void A_seam_or_a_foreign_bound_is_not_refused(string type, string alsoNotNamed) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}'") || error.Message.Contains(alsoNotNamed), consumer.Output);

    /// <summary>
    /// A positional record is told that its parameter list is the constructor, since it declares
    /// none by that name.
    /// </summary>
    [Theory]
    [InlineData("CtorBypass", "the constructor CtorBypass.CtorBypass(string, int)")]
    [InlineData("PositionalStruct", "the parameter list (Guid Value)")]
    [InlineData("PositionalClass", "the parameter list (string Code)")]
    public void A_hand_written_constructor_is_refused_by_name(string type, string constructor) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1009" && error.Message.StartsWith($"'{type}' declares {constructor},"), $"CMTK1009 on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>A static constructor makes no instance, so the value object is still generated.</summary>
    [Fact]
    public void A_static_constructor_is_allowed() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'WithStaticConstructor'"), consumer.Output);

    /// <summary>
    /// The declarations are refused with the errors above, never by an aspect that failed on them:
    /// an exception in an aspect or a compile error in the code it generated surfaces as LAMA0041 or
    /// LAMA0611, which tells the user nothing about their declaration.
    /// </summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones() =>
        consumer.Errors.Select(error => error.Id).Distinct().Order()
                .ShouldBe(["CMTK0001", "CMTK0003", "CMTK0004", "CMTK0005", "CMTK0006", "CMTK0007", "CMTK0008", "CMTK0009", "CMTK1000", "CMTK1001", "CMTK1002", "CMTK1003", "CMTK1004", "CMTK1005", "CMTK1006", "CMTK1007", "CMTK1008", "CMTK1009", "CMTK1010", "CMTK1011", "CMTK1012"], ignoreOrder: false, customMessage: consumer.Output);

    /// <summary>
    /// The <c>GetValue</c>/<c>ValueOrNull</c> companions live in a namespace-level class. Named after
    /// the value object alone, <c>Order.Id</c> and <c>Customer.Id</c> both asked for <c>IdExtensions</c>,
    /// and Metalama crashed (LAMA0001) with the whole project unbuildable and no declaration named.
    /// </summary>
    [Fact]
    public void Nested_value_objects_of_one_name_get_a_companion_class_each() =>
        consumer.Errors.ShouldNotContain(error => error.Id.StartsWith("LAMA") || error.Message.Contains("'Order.Id'") || error.Message.Contains("'Customer.Id'"), consumer.Output);

    [Fact]
    public void The_analyzer_reports_a_default_value_object_beside_the_generators() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK0001" && error.Message.Contains("'Fine'"), consumer.Output);

    /// <summary>
    /// The rules that read calls and members, in the project that declares the value objects. Metalama
    /// runs analyzers on the source before weaving, where a call to a generated member such as
    /// <c>TryFrom</c> or <c>FromKnownGood</c> does not bind: an operation-based rule missed both
    /// (measured 2026-09-28), and the verifier tests, which never weave, cannot see that. The consumer
    /// raises the warnings and the suggestion to errors so they are reported here.
    /// </summary>
    [Theory]
    [InlineData("CMTK0003", "The Option that 'TryFrom' returns")]
    [InlineData("CMTK0004", "Materialize rebuilds 'T'")]
    [InlineData("CMTK0005", "with a default 'Fine'")]
    [InlineData("CMTK0006", "'Unset' starts as a default 'Fine'")]
    [InlineData("CMTK0007", "'Target.FromKnownGood' is given 'input'")]
    [InlineData("CMTK0008", "the value of a 'Fine' with the value of a 'OtherFine'")]
    [InlineData("CMTK0001", "The value object 'Sibling' must be created with 'Sibling.From'")]
    [InlineData("CMTK0003", "The Result that 'Revalidate' returns")]
    [InlineData("CMTK0007", "'Target.FromKnownGood' is given 'named'")]
    [InlineData("CMTK0008", "This compares a 'Sibling' with a 'OtherSibling'")]
    [InlineData("CMTK0009", "'OrDefault' returns a default 'Target' for None")]
    [InlineData("CMTK0009", "'FirstOrDefault' returns a default 'Sibling' when nothing is found")]
    public void A_rule_sees_the_value_objects_of_its_own_project(string id, string fragment) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains(fragment), consumer.Output);

    /// <summary>
    /// A constructor that chains to another through <c>this(…)</c> whose argument calls a generated
    /// member does not bind where Metalama runs analyzers; CMTK0006 resolves it to the one constructor it
    /// can mean, which assigns the member, rather than report it unassigned.
    /// </summary>
    [Fact]
    public void A_constructor_chained_through_a_generated_call_is_not_reported_unassigned() =>
        consumer.Errors.ShouldNotContain(error => error.Id == "CMTK0006" && error.Message.Contains("'Chained()'"), consumer.Output);

    [Fact]
    public void CMTK0002_stays_silent_when_the_real_generators_assembly_is_referenced() =>
        consumer.Errors.ShouldNotContain(error => error.Id == "CMTK0002", consumer.Output);

    public sealed record Diagnostic(string Id, string Message);

    /// <summary>The consumer, built once per test run.</summary>
    public sealed partial class Consumer : IAsyncLifetime
    {
        private const string Declarations =
            """
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            public readonly record struct NotPartial : IValue<int>;

            public readonly partial struct NotRecord : IValue<int>;

            public partial record struct NotReadonly : IValue<int>;

            public readonly partial record struct TwoMarkers : IValue<int>, IValue<string>;

            public enum Fault { Refused }

            public readonly partial record struct Target : IValidatedValue<Target, int, Fault>
            {
                public static Result<Target, Fault> Create(int value) => Result.Error(Fault.Refused);
            }

            public readonly partial record struct Misnamed : IValidatedValue<Target, int, Fault>
            {
                public static Result<Target, Fault> Create(int value) => Target.Create(value);
            }

            public readonly partial record struct Generic<T> : IValue<int>;

            // Nested in a generic type through one that is not: T is still in scope, and the
            // generated code would have to name it where it cannot (an attribute's type argument,
            // a namespace-level companion class).
            public static class Outer<T> { public static class Middle { public readonly partial record struct DeeplyNested : IValue<int>; } }

            public readonly partial record struct ArrayBacked : IValue<int[]>;

            // Violates the notnull constraint, which is only a warning (CS8714). Without its own
            // refusal, the generated JSON converter and parsing fail to compile (LAMA0611/0612).
            public readonly partial record struct NullableBacked : IValue<int?>;

            // A record class that can be derived from: equality across a hierarchy is not value
            // equality, and a derived value object failed inside the generated code (LAMA0611).
            public partial record NotSealed : IValue<string>;

            public sealed partial record DerivesFromAValueObject : NotSealed;

            // A value object that reaches itself through what it wraps has no finite form: its JSON
            // converter serializes the wrapped value through the options, which is its own converter
            // again. As a record class it compiled without a word; as a struct it failed inside the
            // generated code (CS0523 as LAMA0611).
            public sealed partial record WrapsItself : IValue<WrapsItself>;

            public sealed partial record CycleOne : IValue<CycleTwo>;

            public sealed partial record CycleTwo : IValue<CycleOne>;

            public readonly partial record struct StructCycleOne : IValue<StructCycleTwo>;

            public readonly partial record struct StructCycleTwo : IValue<StructCycleOne>;

            // Over another value object, the surface depended on where that one was declared: over one
            // from the same project, parsing, comparison and the type converter were silently missing.
            public readonly partial record struct WrapsAValueObject : IValue<Fine>;

            // Companion classes are named after the whole nesting chain, so these two coexist.
            public sealed class Order { public readonly partial record struct Id : IValue<System.Guid>; }

            public sealed class Customer { public readonly partial record struct Id : IValue<System.Guid>; }

            // A companion class name that is taken, by a declared type or by another value object's
            // companion, is an error naming it, where it was an aspect exception or a crash.
            public readonly partial record struct TakenName : IValue<int>;

            public static class TakenNameExtensions { }

            public readonly partial record struct ShopId : IValue<int>;

            public static class Shop { public readonly partial record struct Id : IValue<int>; }

            // Comparison has one seam, a hand-written CompareTo(TSelf). A hand-written operator failed
            // the aspect (LAMA0500, naming no fix), and a hand-written object overload was kept
            // silently beside the generated generic one, which need not agree with it.
            public readonly partial record struct WithOperator : IValue<int>
            {
                public static bool operator <(WithOperator left, WithOperator right) => left.Value > right.Value;

                public static bool operator >(WithOperator left, WithOperator right) => left.Value < right.Value;
            }

            public readonly partial record struct WithObjectCompareTo : IValue<int>
            {
                public int CompareTo(object? obj) => 0;
            }

            // A value object's only constructor is the generated private one. A hand-written one with
            // its signature failed inside the generated code (LAMA0611), a positional record failed the
            // aspect (LAMA0041), and any other compiled: it skipped Create, or left Value null, and a
            // hand-written copy constructor did that to `with`. The bodies here touch nothing
            // generated, because a refused type is not generated.
            public readonly partial record struct CtorSameSignature : IValue<System.Guid>
            {
                public CtorSameSignature(System.Guid value) { }
            }

            public readonly partial record struct CtorBypass : IValidatedValue<CtorBypass, string, Fault>
            {
                public static Result<CtorBypass, Fault> Create(string value) => Result.Error(Fault.Refused);

                public CtorBypass(string value, int bypass) { }
            }

            public readonly partial record struct CtorUnassignedStruct : IValue<string>
            {
                public CtorUnassignedStruct(int unrelated) { }
            }

            public sealed partial record CtorUnassignedClass : IValue<string>
            {
                public CtorUnassignedClass(int unrelated) { }
            }

            public readonly partial record struct CtorParameterless : IValue<string>
            {
                public CtorParameterless() { }
            }

            public sealed partial record CtorCopy : IValue<string>
            {
                private CtorCopy(CtorCopy original) { }
            }

            public readonly partial record struct PositionalStruct(System.Guid Value) : IValue<System.Guid>;

            public sealed partial record PositionalClass(string Code) : IValue<string>;

            // A static constructor makes no instance: generated as usual.
            public readonly partial record struct WithStaticConstructor : IValue<int>
            {
                static WithStaticConstructor() { }
            }

            // A generic wrapped type, tuples included, failed inside the generated code (LAMA0611).
            public sealed partial record ListBacked : IValue<System.Collections.Generic.List<string>>;

            public readonly partial record struct ImmutableArrayBacked : IValue<System.Collections.Immutable.ImmutableArray<int>>;

            public readonly partial record struct PairBacked : IValue<System.Collections.Generic.KeyValuePair<string, int>>;

            public readonly partial record struct TupleBacked : IValue<(int, int)>;

            public readonly partial record struct NamedTupleBacked : IValue<(int X, int Y)>;

            // Its generated property would have its name (CS0542), and a file-local type crashed
            // Metalama (LAMA0001).
            public static class Named { public readonly partial record struct Value : IValue<int>; }

            file readonly partial record struct FileLocal : IValue<int>;

            // A directive above the declaration, or a file-local type around it: the modifiers were read
            // from the declaration's text with its leading trivia, and of the type alone, and Metalama
            // crashed on both (LAMA0001).
            #region File-local
            file readonly partial record struct FileLocalBelowRegion : IValue<int>;
            #endregion

            #pragma warning disable CS0169
            file readonly partial record struct FileLocalBelowPragma : IValue<int>;
            #pragma warning restore CS0169

            #if NEVER_DEFINED
            public sealed class LeftOut { }
            #endif
            file readonly partial record struct FileLocalBelowDisabledText : IValue<int>;

            [System.Diagnostics.DebuggerDisplay("{Value}")]
            #pragma warning disable CS0169
            file readonly partial record struct FileLocalAfterAttribute : IValue<int>;
            #pragma warning restore CS0169

            file static class FileLocalFixtures
            {
                public readonly partial record struct NestedInFileLocal : IValue<int>;
            }

            // Metalama writes the ToString() override into every part (CS0111 as LAMA0611). The other
            // parts touch nothing generated, because a refused type is not generated.
            public readonly partial record struct SplitInOneFile : IValue<int>;

            public readonly partial record struct SplitInOneFile
            {
                public bool IsSet => true;
            }

            public readonly partial record struct SplitAcrossFiles : IValue<int>;

            // A part a source generator adds does not count, and the value object builds.
            public readonly partial record struct Sku : IValidatedValue<Sku, string, Fault>
            {
                public static Result<Sku, Fault> Create(string value) =>
                    value is not null && Pattern().IsMatch(value) ? new Sku(value) : Result.Error(Fault.Refused);

                [System.Text.RegularExpressions.GeneratedRegex("^[A-Z]{3}-[0-9]{4}$")]
                private static partial System.Text.RegularExpressions.Regex Pattern();
            }

            // A member a generator introduces, written by hand: LAMA0500, LAMA0503, LAMA0512, LAMA0521
            // or LAMA0611, or a Parse kept silently beside the generated parsing.
            public readonly partial record struct HandWrittenFrom : IValue<string>
            {
                public static HandWrittenFrom From(string value) => throw new System.NotSupportedException();
            }

            public readonly partial record struct HandWrittenValue : IValue<string>
            {
                public int Value => 0;
            }

            public readonly partial record struct HandWrittenParse : IValue<int>
            {
                public static HandWrittenParse Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct HandWrittenFormat : IValue<int>
            {
                public string ToString(string? format, System.IFormatProvider? provider) => "";
            }

            public readonly partial record struct HandWrittenMinValue : IValue<int>
            {
                public static int MinValue => 0;
            }

            [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
            public readonly partial record struct HandWrittenJsonConverter : IValue<int>;

            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.Int32Converter))]
            public readonly partial record struct HandWrittenTypeConverter : IValue<int>;

            public readonly partial record struct HandWrittenInterface : IValue<int>, IValueObject<HandWrittenInterface, int>
            {
                public int Value => 0;
            }

            // The generated code calls ExplicitCreate.Create, which cannot reach it (LAMA0611, CS1929).
            public readonly partial record struct ExplicitCreate : IValidatedValue<ExplicitCreate, string, Fault>
            {
                static Result<ExplicitCreate, Fault> IValidatedValue<ExplicitCreate, string, Fault>.Create(string value) => Result.Error(Fault.Refused);
            }

            // Beside a public Create, a second rule set: generic code calling T.Create reached the explicit
            // one, every generated entry point the public one.
            public readonly partial record struct ExplicitCreateBesidePublic : IValidatedValue<ExplicitCreateBesidePublic, string, Fault>
            {
                public static Result<ExplicitCreateBesidePublic, Fault> Create(string value) => Result.Error(Fault.Refused);

                static Result<ExplicitCreateBesidePublic, Fault> IValidatedValue<ExplicitCreateBesidePublic, string, Fault>.Create(string value) => Result.Error(Fault.Refused);
            }

            // An explicit implementation of an interface the generators implement: kept beside the generated
            // public member, it answered every caller through the interface, so a generic T.Parse
            // bypassed Create and Comparer<T>.Default sorted against <. The bodies touch nothing
            // generated, because a refused type is not generated.
            public readonly partial record struct ExplicitParsable : IValidatedValue<ExplicitParsable, string, Fault>, System.IParsable<ExplicitParsable>
            {
                public static Result<ExplicitParsable, Fault> Create(string value) => Result.Error(Fault.Refused);

                static ExplicitParsable System.IParsable<ExplicitParsable>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IParsable<ExplicitParsable>.TryParse(string? s, System.IFormatProvider? provider, out ExplicitParsable result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct ExplicitFormattable : IValue<int>, System.IFormattable
            {
                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public readonly partial record struct ExplicitSpanFormattable : IValue<long>, System.ISpanFormattable
            {
                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";

                bool System.ISpanFormattable.TryFormat(System.Span<char> destination, out int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct ExplicitMinMax : IValue<int>, System.Numerics.IMinMaxValue<ExplicitMinMax>
            {
                static ExplicitMinMax System.Numerics.IMinMaxValue<ExplicitMinMax>.MinValue => throw new System.NotSupportedException();

                static ExplicitMinMax System.Numerics.IMinMaxValue<ExplicitMinMax>.MaxValue => throw new System.NotSupportedException();
            }

            // IConvertible is generated explicitly in full, so declaring it at all, with public or explicit
            // members, failed the aspect (LAMA0041).
            public readonly partial record struct ExplicitConvertible : IValue<int>, System.IConvertible
            {
                public System.TypeCode GetTypeCode() => System.TypeCode.Int32;
                bool System.IConvertible.ToBoolean(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                byte System.IConvertible.ToByte(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                char System.IConvertible.ToChar(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                System.DateTime System.IConvertible.ToDateTime(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                decimal System.IConvertible.ToDecimal(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                double System.IConvertible.ToDouble(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                short System.IConvertible.ToInt16(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                public int ToInt32(System.IFormatProvider? provider) => 0;
                long System.IConvertible.ToInt64(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                sbyte System.IConvertible.ToSByte(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                float System.IConvertible.ToSingle(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                string System.IConvertible.ToString(System.IFormatProvider? provider) => "***";
                object System.IConvertible.ToType(System.Type conversionType, System.IFormatProvider? provider) => throw new System.NotSupportedException();
                ushort System.IConvertible.ToUInt16(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                uint System.IConvertible.ToUInt32(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                ulong System.IConvertible.ToUInt64(System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct ExplicitEqualityOperators : IValue<int>, System.Numerics.IEqualityOperators<ExplicitEqualityOperators, ExplicitEqualityOperators, bool>
            {
                static bool System.Numerics.IEqualityOperators<ExplicitEqualityOperators, ExplicitEqualityOperators, bool>.operator ==(ExplicitEqualityOperators left, ExplicitEqualityOperators right) => true;

                static bool System.Numerics.IEqualityOperators<ExplicitEqualityOperators, ExplicitEqualityOperators, bool>.operator !=(ExplicitEqualityOperators left, ExplicitEqualityOperators right) => false;
            }

            public readonly partial record struct ExplicitComparable : IValue<int>, System.IComparable<ExplicitComparable>
            {
                int System.IComparable<ExplicitComparable>.CompareTo(ExplicitComparable other) => 0;
            }

            public readonly partial record struct ExplicitObjectComparable : IValue<int>, System.IComparable
            {
                int System.IComparable.CompareTo(object? obj) => 0;
            }

            public readonly partial record struct ExplicitComparisonOperators : IValue<int>, System.Numerics.IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>
            {
                static bool System.Numerics.IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>.operator <(ExplicitComparisonOperators left, ExplicitComparisonOperators right) => false;

                static bool System.Numerics.IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>.operator >(ExplicitComparisonOperators left, ExplicitComparisonOperators right) => false;

                static bool System.Numerics.IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>.operator <=(ExplicitComparisonOperators left, ExplicitComparisonOperators right) => false;

                static bool System.Numerics.IComparisonOperators<ExplicitComparisonOperators, ExplicitComparisonOperators, bool>.operator >=(ExplicitComparisonOperators left, ExplicitComparisonOperators right) => false;
            }

            // Where the generators implement no such interface, the explicit implementation is the only
            // one: beside ToString(), the seam, no formatting interface is generated, and a Uri has no
            // ordering. Generated as usual.
            public readonly partial record struct ExplicitFormattableWithToString : IValue<int>, System.IFormattable
            {
                public override string ToString() => "***";

                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public sealed partial record ExplicitComparableUri : IValue<System.Uri>, System.IComparable<ExplicitComparableUri>
            {
                int System.IComparable<ExplicitComparableUri>.CompareTo(ExplicitComparableUri? other) => 0;
            }

            // A hand-written PrintMembers: the generated ToString() replaced the record's, which calls it,
            // so what it hid was printed ("1234", and "PinHolder { Pin = 1234 }" in a record holding it).
            public readonly partial record struct PinStruct : IValue<string>
            {
                private bool PrintMembers(System.Text.StringBuilder builder) { builder.Append("***"); return true; }
            }

            public sealed partial record PinClass : IValue<string>
            {
                private bool PrintMembers(System.Text.StringBuilder builder) { builder.Append("***"); return true; }
            }

            // Beside ToString(), the seam, nothing replaces what calls it: generated as usual.
            public readonly partial record struct PinWithToString : IValue<string>
            {
                public override string ToString()
                {
                    var builder = new System.Text.StringBuilder();
                    PrintMembers(builder);
                    return builder.ToString();
                }

                private bool PrintMembers(System.Text.StringBuilder builder) { builder.Append("***"); return true; }
            }

            // A hand-written equality: the record kept it while the generated ordering, the JSON keys and
            // the column compared the wrapped value, so values it called equal sorted apart.
            public readonly partial record struct CaseInsensitiveEquals : IValue<string>
            {
                public bool Equals(CaseInsensitiveEquals other) => true;

                public override int GetHashCode() => 0;
            }

            public sealed partial record HashCodeOnly : IValidatedValue<HashCodeOnly, string, Fault>
            {
                public static Result<HashCodeOnly, Fault> Create(string value) => Result.Error(Fault.Refused);

                public override int GetHashCode() => 0;
            }

            public readonly partial record struct ExplicitEquatable : IValue<string>, System.IEquatable<ExplicitEquatable>
            {
                bool System.IEquatable<ExplicitEquatable>.Equals(ExplicitEquatable other) => true;
            }

            // Instance state besides the wrapped value: JSON, parsing and the materializer carried the
            // wrapped value alone while the record's equality compared the rest, and a required member
            // failed inside the generated code (LAMA0611, CS9035).
            public sealed partial record WithAutoProperty : IValue<decimal>
            {
                public string Currency { get; init; } = "EUR";
            }

            public sealed partial record WithCacheField : IValidatedValue<WithCacheField, string, Fault>
            {
                private string? _domain;

                public static Result<WithCacheField, Fault> Create(string value) => Result.Error(Fault.Refused);

                public string Domain => _domain ??= "";
            }

            public readonly partial record struct WithRequiredMember : IValue<decimal>
            {
                public required string Currency { get; init; }
            }

            public abstract record Audited
            {
                public System.DateTime At { get; init; }
            }

            public sealed partial record WithInheritedState : Audited, IValue<string>;

            public sealed partial record WithEvent : IValue<int>
            {
                public event System.EventHandler? Changed;
            }

            // Computed from Value, or static, holds nothing besides it: generated as usual.
            public readonly partial record struct WithComputedMembers : IValue<string>
            {
                public const int MaxLength = 10;

                private static readonly string[] Reserved = ["admin"];

                public int Length => Value.Length;

                public bool IsReserved => Reserved.Contains(Value);
            }

            public abstract record Described
            {
                public string Kind => "described";
            }

            public sealed partial record WithStatelessBase : Described, IValue<string>;

            // The seams stay allowed.
            public readonly partial record struct WithEverySeam : IValidatedValue<WithEverySeam, string, Fault>
            {
                public static Result<WithEverySeam, Fault> Create(string value) => new WithEverySeam(value);

                public static Option<WithEverySeam> TryFrom(string value) => Create(value).ToOption();

                public static WithEverySeam FromKnownGood(string value, string? source = null) => new(value);

                public Result<WithEverySeam, Fault> Revalidate() => Create(Value);

                public int CompareTo(WithEverySeam other) => string.CompareOrdinal(Value, other.Value);

                public override string ToString() => "***";
            }

            // A MinValue of another type, or one the value object cannot reach, is no bound: nothing is
            // generated for it, where it failed inside the generated code (LAMA0611, CS1503 and CS0122).
            public readonly record struct Percent(decimal Amount)
            {
                public const decimal MinValue = 0m;
                public const decimal MaxValue = 100m;
            }

            public readonly partial record struct Share : IValue<Percent>;

            public readonly record struct Level(int Rung)
            {
                private static readonly Level MinValue = default;
                private static readonly Level MaxValue = default;
            }

            public readonly partial record struct Rank : IValue<Level>;

            // A dictionary key is read by JsonMetadataServices.{TypeName}Converter, named after the
            // wrapped type. The probes cover int and decimal; a misnamed converter for any other number
            // would fail to compile here, as LAMA0611, rather than in a consumer's build.
            public readonly partial record struct AByte : IValue<byte>;
            public readonly partial record struct ASByte : IValue<sbyte>;
            public readonly partial record struct AShort : IValue<short>;
            public readonly partial record struct AUShort : IValue<ushort>;
            public readonly partial record struct AUInt : IValue<uint>;
            public readonly partial record struct ALong : IValue<long>;
            public readonly partial record struct AULong : IValue<ulong>;
            public readonly partial record struct AFloat : IValue<float>;
            public readonly partial record struct ADouble : IValue<double>;
            public readonly partial record struct AHalf : IValue<System.Half>;
            public readonly partial record struct AnInt128 : IValue<System.Int128>;
            public readonly partial record struct AUInt128 : IValue<System.UInt128>;

            public readonly partial record struct Fine : IValue<int>;

            public readonly partial record struct OtherFine : IValue<int>;

            public static class Uses
            {
                public static Fine Make() => default;

                public static WithStaticConstructor MakeWithStaticConstructor() => WithStaticConstructor.From(1);
            }

            public static class SameProject
            {
                public static void IgnoredTryFrom(int input) { Target.TryFrom(input); }

                public static Target KnownGoodFromInput(int input) => Target.FromKnownGood(input);

                public static Fine[] Slots(int count) => new Fine[count];

                public static T Materialized<T>(int value) where T : IValueObjectMaterializer<T, int> => T.Materialize(value);

                public static bool Mixed(Fine fine, OtherFine other) => fine.Value == other.Value;
            }

            public sealed class Holder
            {
                public Fine Unset { get; set; }
            }

            public readonly partial record struct Sibling : IValue<int>;

            public readonly partial record struct OtherSibling : IValue<int>;

            public static class SameProjectForms
            {
                public static Sibling ConditionalDefault(bool pick) => pick ? Sibling.From(1) : default;

                public static void IgnoredRevalidate(Target target) { target.Revalidate(); }

                public static Target KnownGoodNamed(int named) => Target.FromKnownGood(value: named);

                public static bool MixedObjects(Sibling sibling, OtherSibling other) => sibling.Equals(other);

                public static Target OrDefaultOfTryFrom(int input) => Target.TryFrom(input).OrDefault();

                public static Sibling FirstSibling(List<Sibling> siblings) => siblings.FirstOrDefault();
            }

            public sealed class Chained
            {
                public Chained(Fine id) => Id = id;

                public Chained() : this(Fine.From(1)) { }

                public Fine Id { get; }
            }
            """;

        /// <summary>
        /// Under the ignored <c>artifacts/</c> rather than the temp folder: on macOS that is behind the
        /// <c>/var</c> symlink, and NuGet's relative project paths then resolve against the wrong side.
        /// </summary>
        private readonly string _directory = Path.Combine(RepositoryRoot(), "artifacts", $"consumer-{Guid.NewGuid():N}");

        /// <summary>A second file, so a declaration can be split across files.</summary>
        private const string OtherFile =
            """
            namespace Consumer;

            public readonly partial record struct SplitAcrossFiles
            {
                public bool IsSet => true;
            }

            public readonly partial record struct SplitAcrossFiles
            {
                public bool IsUnset => false;
            }
            """;

        public IReadOnlyList<Diagnostic> Errors { get; private set; } = [];

        public string Output { get; private set; } = "";

        public async ValueTask InitializeAsync()
        {
            Directory.CreateDirectory(_directory);

            var src = Path.Combine(RepositoryRoot(), "src");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Consumer.csproj"),
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <Nullable>enable</Nullable>
                     <ImplicitUsings>enable</ImplicitUsings>
                   </PropertyGroup>
                   <ItemGroup>
                     <ProjectReference Include="{src}/CodoMetis.TypeKit.Generators/CodoMetis.TypeKit.Generators.csproj" />
                     <ProjectReference Include="{src}/CodoMetis.TypeKit.Analyzers/CodoMetis.TypeKit.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                   </ItemGroup>
                 </Project>
                 """);
            await File.WriteAllTextAsync(Path.Combine(_directory, "Declarations.cs"), Declarations);
            await File.WriteAllTextAsync(Path.Combine(_directory, "OtherFile.cs"), OtherFile);

            // Only errors are read, so the rules that ship as warnings or a suggestion are raised here.
            await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"),
                """
                root = true

                [*.cs]
                dotnet_diagnostic.CMTK0003.severity = error
                dotnet_diagnostic.CMTK0005.severity = error
                dotnet_diagnostic.CMTK0006.severity = error
                dotnet_diagnostic.CMTK0007.severity = error
                dotnet_diagnostic.CMTK0008.severity = error
                dotnet_diagnostic.CMTK0009.severity = error
                """);

            // Stop MSBuild's upward search here, so the repository's own build settings (warnings as
            // errors, documentation, central package management) do not apply to the consumer.
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            // --no-dependencies and RestoreRecursive=false: the referenced projects are already built
            // by this test run, and restoring them again could rewrite restore output that other
            // tests read concurrently. Already built in this run's configuration, which is therefore
            // passed on: without it the consumer builds in Debug and, in a Release run such as CI's,
            // finds no referenced assembly at all.
            using var process = Process.Start(new ProcessStartInfo(
                "dotnet", ["build", _directory, "-c", Configuration, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                Environment            = { ["DOTNET_CLI_UI_LANGUAGE"] = "en" },
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Output = await output + await errors;
            Errors = [.. DiagnosticLine().Matches(Output).Select(match => new Diagnostic(match.Groups["id"].Value, match.Groups["message"].Value)).Distinct()];

            Errors.ShouldNotBeEmpty($"The consumer build reported no errors at all, so it did not run as intended:{Environment.NewLine}{Output}");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }

        /// <summary>The configuration this test assembly, and so the projects it references, was built in.</summary>
        private static string Configuration =>
            typeof(Consumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
         ?? throw new InvalidOperationException("The test assembly carries no AssemblyConfigurationAttribute.");

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx"))) return directory.FullName;
            }

            throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
        }

        [GeneratedRegex(@"error (?<id>[A-Z]+\d+): (?<message>.*?)(?: \[|$)", RegexOptions.Multiline)]
        private static partial Regex DiagnosticLine();
    }
}
