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
    [InlineData("CMTK1005", "ObjectBacked")]
    [InlineData("CMTK1005", "EnumBacked")]
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
    [InlineData("CMTK1005", "VoNames.Parse")]
    [InlineData("CMTK1005", "VoNames.From")]
    [InlineData("CMTK1005", "VoNames.TryFrom")]
    [InlineData("CMTK1005", "VoNames.CompareTo")]
    [InlineData("CMTK1005", "VoNames.MinValue")]
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
    [InlineData("CMTK1008", "InheritsExplicitComparable")]
    [InlineData("CMTK1008", "InheritsExplicitObjectComparable")]
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
    [InlineData("CMTK1011", "HandWrittenParseOverASet")]
    [InlineData("CMTK1011", "HandWrittenFormat")]
    [InlineData("CMTK1011", "HandWrittenMinValue")]
    [InlineData("CMTK1011", "HandWrittenJsonConverter")]
    [InlineData("CMTK1011", "HandWrittenTypeConverter")]
    [InlineData("CMTK1011", "HandWrittenOrdering")]
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
    [InlineData("CMTK1011", "InheritsUnsealedToString")]
    [InlineData("CMTK1011", "InheritsPrintMembers")]
    [InlineData("CMTK1011", "InheritsExplicitParsable")]
    [InlineData("CMTK1011", "InheritsExplicitFormattable")]
    [InlineData("CMTK1011", "InheritsExplicitMinMax")]
    [InlineData("CMTK1011", "InheritsExplicitEqualityOperators")]
    [InlineData("CMTK1011", "InheritsConvertible")]
    [InlineData("CMTK1012", "WithAutoProperty")]
    [InlineData("CMTK1012", "WithCacheField")]
    [InlineData("CMTK1012", "WithRequiredMember")]
    [InlineData("CMTK1012", "WithInheritedState")]
    [InlineData("CMTK1012", "WithEvent")]
    [InlineData("CMTK1013", "SetNotPartial")]
    [InlineData("CMTK1014", "SetNotSealed")]
    [InlineData("CMTK1015", "SetWithoutMembers")]
    [InlineData("CMTK1016", "SetWithPublicConstructor")]
    [InlineData("CMTK1017", "SetWithProperty")]
    [InlineData("CMTK1017", "SetWithMutableField")]
    [InlineData("CMTK1017", "SetWithInternalMember")]
    [InlineData("CMTK1018", "SetGeneric")]
    [InlineData("CMTK1016", "SetWithInternalConstructor")]
    [InlineData("CMTK1019", "SetWithAlias.Legacy")]
    [InlineData("CMTK1019", "SetWithFactoryMember.B")]
    [InlineData("CMTK1019", "SetWithUninitializedMember.B")]
    [InlineData("CMTK1019", "SetWithTrailingCall.B")]
    [InlineData("CMTK1020", "SetRecord")]
    [InlineData("CMTK1014", "SetAbstract")]
    [InlineData("CMTK1022", "AbstractRecord")]
    [InlineData("CMTK1022", "AbstractClass")]
    [InlineData("CMTK1022", "AbstractValidated")]
    [InlineData("CMTK1022", "AbstractThroughInterface")]
    [InlineData("CMTK1022", "AbstractHolder.AbstractNested")]
    [InlineData("CMTK1011", "SetWithName")]
    [InlineData("CMTK1011", "SetWithExplicitAll")]
    [InlineData("CMTK1011", "SetWithParsable")]
    [InlineData("CMTK1011", "SetWithTypeConverterAttribute")]
    [InlineData("CMTK1011", "SetWithJsonConverterAttribute")]
    [InlineData("CMTK1021", "SetWithMemberNamedParse.Parse")]
    [InlineData("CMTK1021", "SetWithMemberNamedToString.ToString")]
    [InlineData("CMTK1021", "SetWithMemberNamedAll.All")]
    [InlineData("CMTK1021", "SetWithCasedMembers.open")]
    [InlineData("CMTK1021", "SetWithCaseVariants.EXPRESS")]
    [InlineData("CMTK1021", "SetNames.Name")]
    [InlineData("CMTK1021", "SetNames.Match")]
    [InlineData("CMTK1011", "SetWithIgnoreCaseTryParse")]
    [InlineData("CMTK1011", "SetWithIgnoreCaseParse")]
    public void A_declaration_that_cannot_be_generated_is_an_error(string id, string type) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains($"'{type}"), $"{id} on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// A member named as a generated member failed the aspect (LAMA0506 beside Match, Parse and
    /// TryParse, LAMA0611 beside ToString), or was CMTK1011 telling the author to remove it. Two members
    /// whose names differ only in case are one name to <c>TryParse(name, ignoreCase: true)</c>; where
    /// the difference is the first letter's, they also gave Match one parameter twice (LAMA0611, CS0100),
    /// and where it is elsewhere (<c>Express</c>, <c>EXPRESS</c>) the set was generated and the
    /// ignore-case parse found the first declared.
    /// </summary>
    [Theory]
    [InlineData("SetWithMemberNamedParse.Parse", "has the name of a member the generators introduce")]
    [InlineData("SetWithMemberNamedAll.All", "has the name of a member the generators introduce")]
    [InlineData("SetWithCasedMembers.open", "differs from 'SetWithCasedMembers.Open' only in case, which Match's parameters and TryParse(name, ignoreCase: true) cannot tell apart")]
    [InlineData("SetWithCaseVariants.EXPRESS", "differs from 'SetWithCaseVariants.Express' only in case, which Match's parameters and TryParse(name, ignoreCase: true) cannot tell apart")]
    public void A_member_name_the_generators_take_is_refused_with_the_reason(string member, string reason) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1021" && error.Message.StartsWith($"'{member}' {reason}, so ") && error.Message.Contains("rename the member"), consumer.Output);

    /// <summary>
    /// What the generators implement and attach, written by hand, failed the aspect or the generated
    /// code: <c>IParsable</c> twice, an attribute twice (CS0579). The error names each.
    /// </summary>
    [Theory]
    [InlineData("SetWithParsable", "the interface IParsable<SetWithParsable>")]
    [InlineData("SetWithTypeConverterAttribute", "the attribute TypeConverter")]
    [InlineData("SetWithJsonConverterAttribute", "the attribute JsonConverter")]
    public void What_the_generators_implement_or_attach_is_refused_by_name_on_a_set(string type, string declared) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(declared), consumer.Output);

    [Fact]
    public void A_member_is_never_told_to_be_removed_as_a_hand_written_generated_member() =>
        consumer.Errors.ShouldNotContain(error => error.Id == "CMTK1011" && error.Message.Contains("SetWithMemberNamed"), consumer.Output);

    /// <summary>
    /// Before weaving, where Metalama runs analyzers, the generated Match does not exist in the project
    /// that declares the set; CMTK0012 still reports a call by position, with the parameter names the
    /// generators give, which the named call beside it compiles with.
    /// </summary>
    [Fact]
    public void CMTK0012_reports_a_call_by_position_with_the_generators_names_in_the_declaring_project() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK0012" && error.Message.StartsWith("'SetToMatch.Match' is given an argument by position") && error.Message.Contains("(@new:, open:)"), consumer.Output);

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
    /// A type named as a member the generators introduce into it failed inside the generated code
    /// (LAMA0611, CS0542: a member cannot have the name of its type), naming nothing the user wrote:
    /// a closed set named <c>Name</c>, a value object named <c>Parse</c>. Each is refused by name.
    /// </summary>
    [Theory]
    [InlineData("CMTK1021", "SetNames.Name", "'SetNames.Name' has the name of a member the generators introduce into it, and a member cannot have the name of its type, so 'SetNames.Name' is not generated: rename the set.")]
    [InlineData("CMTK1005", "VoNames.Parse", "'VoNames.Parse' cannot be generated as a value object: a value object cannot be named 'Parse', the name of a member the generators introduce, since a member cannot have the name of its type; rename it.")]
    public void A_type_named_as_a_generated_member_is_refused_by_name(string id, string type, string message) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.StartsWith(message), $"{id} on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// <c>System.Enum</c> is no enum: its generic <c>Enum.Parse&lt;TEnum&gt;(string)</c> was taken for a
    /// <c>Parse</c> of its own, and the generated parsing failed (LAMA0611, CS0411).
    /// </summary>
    [Fact]
    public void A_value_object_over_System_Enum_is_refused_with_the_reason() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1005" && error.Message.StartsWith("'EnumBacked' cannot be generated as a value object: the wrapped type 'Enum' is the base of every enum and no enum itself; wrap the enum type"), consumer.Output);

    /// <summary>
    /// The companion class names the value object outside it, and an <c>[Obsolete(error: true)]</c> on
    /// the value object, or on a type it is nested in, failed there (LAMA0611, CS0619). The companion
    /// carries the attribute now, and inside an obsolete declaration C# reports no use of one.
    /// </summary>
    [Theory]
    [InlineData("Retired")]
    [InlineData("RetiredHolder")]
    public void An_obsolete_value_object_is_generated(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}"), consumer.Output);

    /// <summary>
    /// A value object over <c>object</c> compiled, and its converter wrote <c>OverObject.From(5)</c> as
    /// <c>{}</c> where an <c>object</c> property holding 5 is written as 5: the serializer writes an
    /// object by its run-time type, which a converter called directly does not.
    /// </summary>
    [Fact]
    public void A_value_object_over_object_is_refused_with_the_reason() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1005" && error.Message.StartsWith("'ObjectBacked' cannot be generated as a value object: the wrapped type 'object' holds a value of any type")
                                            && error.Message.Contains("wrap the type the value has"), consumer.Output);

    /// <summary>
    /// An interface or an abstract class is written and read as the serializer writes and reads it (its
    /// declared members, polymorphic where the type declares it, refused on reading where the serializer
    /// refuses it), so it is wrapped. A guard against the refusal of <c>object</c> reaching further. A type
    /// whose only static <c>Parse(string)</c> is generic is wrapped without parsing: the generic one was
    /// taken for its own and failed inside the generated code (LAMA0611, CS0411), as for <c>System.Enum</c>.
    /// </summary>
    [Theory]
    [InlineData("InterfaceBacked")]
    [InlineData("AbstractClassBacked")]
    [InlineData("GenericParserBacked")]
    public void An_interface_or_an_abstract_class_is_wrapped(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}'"), consumer.Output);

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
    [InlineData("HandWrittenParseOverASet", "HandWrittenParseOverASet.Parse(string, IFormatProvider?)")]
    [InlineData("HandWrittenFormat", "HandWrittenFormat.ToString(string?, IFormatProvider?)")]
    [InlineData("HandWrittenMinValue", "HandWrittenMinValue.MinValue")]
    [InlineData("HandWrittenJsonConverter", "the attribute JsonConverter")]
    [InlineData("HandWrittenTypeConverter", "the attribute TypeConverter")]
    [InlineData("HandWrittenOrdering", "the attribute HandWrittenOrdering")]
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
    /// What a base record declares counts as the value object's own. Its unsealed <c>ToString()</c> and its
    /// <c>PrintMembers</c> were replaced by the generated <c>ToString()</c>, printing what they hid; an
    /// explicit implementation of a generated interface was replaced without a word (<c>IFormattable</c>,
    /// <c>IEqualityOperators</c>) or kept answering the interface (<c>IParsable</c>, <c>IMinMaxValue</c>,
    /// <c>IComparable</c>): a generic <c>T.Parse</c> skipped <c>Create</c>. <c>IConvertible</c> on a base
    /// failed the aspect (LAMA0041). Measured 2026-09-28.
    /// </summary>
    [Theory]
    [InlineData("CMTK1011", "InheritsUnsealedToString", "MaskingBase.ToString() in its base type, which is not sealed, so it is not generated: its generated ToString() replaces it")]
    [InlineData("CMTK1011", "InheritsPrintMembers", "PrintingBase.PrintMembers(StringBuilder) in its base type PrintingBase, so it is not generated")]
    [InlineData("CMTK1011", "InheritsExplicitParsable", "the explicit implementation of IParsable<InheritsExplicitParsable>.Parse(string, IFormatProvider?) in its base type ParsableBase<InheritsExplicitParsable>")]
    [InlineData("CMTK1011", "InheritsExplicitFormattable", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its base type FormattableBase")]
    [InlineData("CMTK1011", "InheritsExplicitMinMax", "the explicit implementation of IMinMaxValue<InheritsExplicitMinMax>.MinValue in its base type MinMaxBase<InheritsExplicitMinMax>")]
    [InlineData("CMTK1011", "InheritsExplicitEqualityOperators", "in its base type EqualityOperatorsBase<InheritsExplicitEqualityOperators>")]
    [InlineData("CMTK1011", "InheritsConvertible", "the interface IConvertible in its base type ConvertibleBase")]
    [InlineData("CMTK1008", "InheritsExplicitComparable", "the explicit implementation of IComparable<InheritsExplicitComparable>.CompareTo(InheritsExplicitComparable?) in its base type ComparableBase<InheritsExplicitComparable>")]
    [InlineData("CMTK1008", "InheritsExplicitObjectComparable", "the explicit implementation of IComparable.CompareTo(object?) in its base type ObjectComparableBase")]
    public void What_a_base_record_declares_is_refused_by_name(string id, string type, string member) =>
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
    [InlineData("InheritsSealedToString", "'InheritsSealedToString")]
    [InlineData("InheritsFormattableBesideSealedToString", "'InheritsFormattableBesideSealedToString")]
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

    /// <summary>
    /// An abstract type with a marker was given no aspect and no word: it compiled with no member
    /// generated, and a value held as it, of a sealed type derived from it, had no <c>Value</c> and was
    /// written as <c>{}</c>. It is refused where it is declared, once, as a value object it cannot be.
    /// </summary>
    [Fact]
    public void An_abstract_value_object_is_refused_once_with_the_reason() =>
        consumer.Errors.Where(error => error.Message.Contains("'AbstractRecord'"))
                .ShouldHaveSingleItem(consumer.Output)
                .ShouldSatisfyAllConditions(
                    error => error.Id.ShouldBe("CMTK1022"),
                    error => error.Message.ShouldStartWith("'AbstractRecord' is abstract, so it is not generated: an abstract type cannot be a value object. A value held as 'AbstractRecord' would have no Value, and its JSON would be '{}'."));

    /// <summary>A static constructor makes no instance, so the value object is still generated.</summary>
    [Fact]
    public void A_static_constructor_is_allowed() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'WithStaticConstructor'"), consumer.Output);

    /// <summary>
    /// The declarations are refused with the errors above, never by an aspect that failed on them:
    /// an exception in an aspect or a compile error in the code it generated surfaces as LAMA0041 or
    /// LAMA0611, which tells the user nothing about their declaration. CS0535 is the one compiler
    /// error that accompanies a refused closed set: its interface has members the generators would
    /// have implemented, and the CMTK error beside it says why they did not. A declaration whose error is
    /// the compiler's own is built apart (<see cref="EqualityOperatorBuildTests"/>).
    /// </summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones() =>
        consumer.Errors.Select(error => error.Id).Distinct().Order()
                .ShouldBe(["CMTK0001", "CMTK0003", "CMTK0004", "CMTK0005", "CMTK0006", "CMTK0007", "CMTK0008", "CMTK0009", "CMTK0012", "CMTK1000", "CMTK1001", "CMTK1002", "CMTK1003", "CMTK1004", "CMTK1005", "CMTK1006", "CMTK1007", "CMTK1008", "CMTK1009", "CMTK1010", "CMTK1011", "CMTK1012", "CMTK1013", "CMTK1014", "CMTK1015", "CMTK1016", "CMTK1017", "CMTK1018", "CMTK1019", "CMTK1020", "CMTK1021", "CMTK1022", "CS0535"], ignoreOrder: false, customMessage: consumer.Output);

    /// <summary>
    /// CS0535 is allowed above only for a refused closed set, whose interface members the generators
    /// did not implement. A value object or a generated set that lost a member the same way is a
    /// regression the list of ids alone would let through.
    /// </summary>
    /// <summary>
    /// A compiler error in a project hid every LAMA0611 inside a declaration's own file, so the build
    /// that holds the value objects has none: what an aspect generated that does not compile is seen
    /// there, by <see cref="Every_error_is_one_of_the_intended_ones"/>.
    /// </summary>
    [Fact]
    public void The_declarations_build_without_a_compiler_error() =>
        consumer.DeclarationErrors.ShouldNotContain(error => error.Id.StartsWith("CS", StringComparison.Ordinal), consumer.Output);

    [Fact]
    public void CS0535_names_only_a_refused_closed_set() =>
        consumer.Errors.Where(error => error.Id == "CS0535")
                .ShouldAllBe(error => RefusedClosedSet().IsMatch(error.Message), consumer.Output);

    [GeneratedRegex(@"^'(?:Consumer\.)?Set\w+(?:\.\w+)?(?:<T>)?' does not implement interface member ")]
    private static partial Regex RefusedClosedSet();

    /// <summary>
    /// A generator error cannot carry a help link as an analyzer rule does (a Metalama 2026.1
    /// <c>DiagnosticDefinition</c> takes none), so its message ends with one, to the README's Build
    /// errors table. A link to a heading that does not exist opens the top of the page, silently.
    /// </summary>
    [Fact]
    public void Every_build_error_links_to_the_Build_errors_table()
    {
        const string link = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors";

        var errors = consumer.Errors.Where(error => error.Id.StartsWith("CMTK1", StringComparison.Ordinal)).ToList();

        errors.Select(error => error.Id).Distinct().Count().ShouldBe(23, $"Not every CMTK1000-CMTK1022 error was observed, so the check below is incomplete:{Environment.NewLine}{consumer.Output}");
        errors.Where(error => !error.Message.EndsWith($". See {link}", StringComparison.Ordinal))
              .Select(error => $"{error.Id}: {error.Message}")
              .ShouldBeEmpty("These errors do not end with the link to the Build errors table.");
        File.ReadAllLines(Path.Combine(Consumer.RepositoryRoot(), "src", "CodoMetis.TypeKit.Generators", "README.md"))
            .ShouldContain("## Build errors", "The link's anchor, #build-errors, is no heading of the Generators README.");
    }

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
            using CodoMetis.TypeKit.ClosedSets;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            // Generated; the calls below are CMTK0012's, in the project that declares the set.
            public sealed partial class SetToMatch : IClosedSet<SetToMatch> { public static readonly SetToMatch New = new(); public static readonly SetToMatch Open = new(); }

            public static class MatchCalls
            {
                public static int ByPosition(SetToMatch set) => set.Match(() => 1, () => 2);

                public static int Named(SetToMatch set) => set.Match(@new: () => 1, open: () => 2);
            }

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

            // Written as {} where an object property holding the same value is written as that value.
            public readonly partial record struct ObjectBacked : IValue<object>;

            // Written and read as the serializer writes and reads the declared type: wrapped.
            public readonly partial record struct InterfaceBacked : IValue<System.IDisposable>;

            public readonly partial record struct AbstractClassBacked : IValue<System.IO.Stream>;

            public sealed class GenericParser { public static T Parse<T>(string text) where T : new() => new(); }

            public readonly partial record struct GenericParserBacked : IValue<GenericParser>;

            // A record class that can be derived from: equality across a hierarchy is not value
            // equality, and a derived value object failed inside the generated code (LAMA0611).
            public partial record NotSealed : IValue<string>;

            public sealed partial record DerivesFromAValueObject : NotSealed;

            // Abstract: given no aspect and no word, while a value held as one, of a sealed type
            // derived from it, had no Value and was written as {}.
            public abstract partial record AbstractRecord : IValue<string>;

            public sealed partial record DerivesFromAbstract : AbstractRecord;

            public abstract class AbstractClass : IValue<int> { }

            public abstract partial record AbstractValidated : IValidatedValue<AbstractValidated, int, Fault>
            {
                public static Result<AbstractValidated, Fault> Create(int value) => Result.Error(Fault.Refused);
            }

            public interface IAbstractIdentifier : IValue<System.Guid> { }

            public abstract partial record AbstractThroughInterface : IAbstractIdentifier;

            public static class AbstractHolder { public abstract partial record AbstractNested : IValue<long>; }

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

            // Over a closed set of this project the value object parses through the set, which it sees
            // because ClosedSetAspect runs first; a Parse of its own was kept silently while it did not.
            public sealed partial class SetToWrap : IClosedSet<SetToWrap> { public static readonly SetToWrap A = new(); }

            public readonly partial record struct HandWrittenParseOverASet : IValue<SetToWrap>
            {
                public static HandWrittenParseOverASet Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

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

            // Named as a member the generators introduce: LAMA0611 (CS0542) inside the generated code.
            public static class VoNames
            {
                public readonly partial record struct Parse : IValue<int>;

                public readonly partial record struct From : IValue<int>;

                public readonly partial record struct TryFrom : IValidatedValue<TryFrom, int, Fault>
                {
                    public static Result<TryFrom, Fault> Create(int value) => Result.Error(Fault.Refused);
                }

                public readonly partial record struct CompareTo : IValue<int>;

                public readonly partial record struct MinValue : IValue<int>;
            }

            // Enum.Parse<TEnum>(string) was taken for a Parse of its own: LAMA0611 (CS0411).
            public readonly partial record struct EnumBacked : IValue<System.Enum>;

            // The companion class named the value object outside it: LAMA0611 (CS0619).
            [System.Obsolete("Retired.", error: true)]
            public readonly partial record struct Retired : IValue<int>;

            [System.Obsolete("Retired.", error: true)]
            public static class RetiredHolder { public readonly partial record struct Inner : IValue<int>; }

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

            // Says the ordering is hand-written where it is not, and EF would refuse to order it.
            [CodoMetis.TypeKit.CompilerServices.HandWrittenOrdering]
            public readonly partial record struct HandWrittenOrdering : IValue<int>;

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

            // What a base record declares: the generated ToString() replaced an unsealed ToString() and
            // the PrintMembers a derived record's ToString() reaches; an explicit implementation of a
            // generated interface was replaced or kept answering the interface; IConvertible failed the
            // aspect (LAMA0041).
            public abstract record MaskingBase
            {
                public override string ToString() => "***";
            }

            public sealed partial record InheritsUnsealedToString : MaskingBase, IValue<string>;

            public abstract record PrintingBase
            {
                protected virtual bool PrintMembers(System.Text.StringBuilder builder) { builder.Append("***"); return true; }
            }

            public sealed partial record InheritsPrintMembers : PrintingBase, IValue<string>;

            public abstract record ParsableBase<TSelf> : System.IParsable<TSelf> where TSelf : ParsableBase<TSelf>, System.IParsable<TSelf>
            {
                static TSelf System.IParsable<TSelf>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IParsable<TSelf>.TryParse(string? s, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsExplicitParsable : ParsableBase<InheritsExplicitParsable>, IValidatedValue<InheritsExplicitParsable, string, Fault>
            {
                public static Result<InheritsExplicitParsable, Fault> Create(string value) => Result.Error(Fault.Refused);
            }

            public abstract record FormattableBase : System.IFormattable
            {
                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public sealed partial record InheritsExplicitFormattable : FormattableBase, IValue<int>;

            public abstract record MinMaxBase<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : MinMaxBase<TSelf>, System.Numerics.IMinMaxValue<TSelf>
            {
                static TSelf System.Numerics.IMinMaxValue<TSelf>.MinValue => throw new System.NotSupportedException();

                static TSelf System.Numerics.IMinMaxValue<TSelf>.MaxValue => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsExplicitMinMax : MinMaxBase<InheritsExplicitMinMax>, IValue<int>;

            public abstract record EqualityOperatorsBase<TSelf> : System.Numerics.IEqualityOperators<TSelf, TSelf, bool> where TSelf : EqualityOperatorsBase<TSelf>, System.Numerics.IEqualityOperators<TSelf, TSelf, bool>
            {
                static bool System.Numerics.IEqualityOperators<TSelf, TSelf, bool>.operator ==(TSelf? left, TSelf? right) => true;

                static bool System.Numerics.IEqualityOperators<TSelf, TSelf, bool>.operator !=(TSelf? left, TSelf? right) => false;
            }

            public sealed partial record InheritsExplicitEqualityOperators : EqualityOperatorsBase<InheritsExplicitEqualityOperators>, IValue<int>;

            public abstract record ConvertibleBase : System.IConvertible
            {
                public System.TypeCode GetTypeCode() => System.TypeCode.Int32;
                bool System.IConvertible.ToBoolean(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                byte System.IConvertible.ToByte(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                char System.IConvertible.ToChar(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                System.DateTime System.IConvertible.ToDateTime(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                decimal System.IConvertible.ToDecimal(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                double System.IConvertible.ToDouble(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                short System.IConvertible.ToInt16(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                int System.IConvertible.ToInt32(System.IFormatProvider? provider) => 0;
                long System.IConvertible.ToInt64(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                sbyte System.IConvertible.ToSByte(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                float System.IConvertible.ToSingle(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                string System.IConvertible.ToString(System.IFormatProvider? provider) => "***";
                object System.IConvertible.ToType(System.Type conversionType, System.IFormatProvider? provider) => throw new System.NotSupportedException();
                ushort System.IConvertible.ToUInt16(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                uint System.IConvertible.ToUInt32(System.IFormatProvider? provider) => throw new System.NotSupportedException();
                ulong System.IConvertible.ToUInt64(System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsConvertible : ConvertibleBase, IValue<int>;

            public abstract record ComparableBase<TSelf> : System.IComparable<TSelf> where TSelf : ComparableBase<TSelf>
            {
                int System.IComparable<TSelf>.CompareTo(TSelf? other) => 1;
            }

            public sealed partial record InheritsExplicitComparable : ComparableBase<InheritsExplicitComparable>, IValue<int>;

            public abstract record ObjectComparableBase : System.IComparable
            {
                int System.IComparable.CompareTo(object? obj) => 1;
            }

            public sealed partial record InheritsExplicitObjectComparable : ObjectComparableBase, IValue<int>;

            // A base record's sealed ToString() is the seam, as C# keeps it in every derived record: it failed
            // the aspect (LAMA0502). Beside it no formatting interface is generated, so an explicit one on the
            // base is the only one. Generated as usual.
            public abstract record SealedMaskingBase
            {
                public sealed override string ToString() => "***";
            }

            public sealed partial record InheritsSealedToString : SealedMaskingBase, IValue<string>;

            public abstract record SealedFormattableBase : System.IFormattable
            {
                public sealed override string ToString() => "***";

                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public sealed partial record InheritsFormattableBesideSealedToString : SealedFormattableBase, IValue<int>;

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
        /// The closed sets the generators refuse, built apart: a refused set does not implement its
        /// interface (CS0535), and beside a compiler error Metalama reported no error in the code it
        /// generated into a declaration's own file, so in one project with the value objects a LAMA0611
        /// there went unseen (measured 2026-10-13 with a generic static Parse taken for the wrapped type's).
        /// </summary>
        private const string RefusedSets =
            """
            using CodoMetis.TypeKit.ClosedSets;

            namespace Consumer;

            public sealed class SetNotPartial : IClosedSet<SetNotPartial> { public static readonly SetNotPartial A = new(); }

            public partial class SetNotSealed : IClosedSet<SetNotSealed> { public static readonly SetNotSealed A = new(); }

            public sealed partial class SetWithoutMembers : IClosedSet<SetWithoutMembers> { }

            public sealed partial class SetWithPublicConstructor : IClosedSet<SetWithPublicConstructor> { public static readonly SetWithPublicConstructor A = new(); public SetWithPublicConstructor() { } }

            public sealed partial class SetWithProperty : IClosedSet<SetWithProperty> { public static readonly SetWithProperty A = new(); public static SetWithProperty B => A; }

            public sealed partial class SetWithMutableField : IClosedSet<SetWithMutableField> { public static readonly SetWithMutableField A = new(); public static SetWithMutableField B = A; }

            public sealed partial class SetGeneric<T> : IClosedSet<SetGeneric<T>> { public static readonly SetGeneric<T> A = new(); }

            public sealed partial class SetWithInternalConstructor : IClosedSet<SetWithInternalConstructor> { public static readonly SetWithInternalConstructor A = new(); internal SetWithInternalConstructor() { } }

            public sealed partial class SetWithAlias : IClosedSet<SetWithAlias> { public static readonly SetWithAlias Open = new(); public static readonly SetWithAlias Legacy = Open; }

            public sealed partial class SetWithFactoryMember : IClosedSet<SetWithFactoryMember> { public static readonly SetWithFactoryMember A = new(); public static readonly SetWithFactoryMember B = Make(); private static SetWithFactoryMember Make() => A; }

            public sealed partial class SetWithUninitializedMember : IClosedSet<SetWithUninitializedMember> { public static readonly SetWithUninitializedMember A = new(); public static readonly SetWithUninitializedMember B; static SetWithUninitializedMember() { B = new(); } }

            public sealed partial class SetWithTrailingCall : IClosedSet<SetWithTrailingCall> { public static readonly SetWithTrailingCall A = new(); public static readonly SetWithTrailingCall B = new SetWithTrailingCall().Same(); private SetWithTrailingCall Same() => A; }

            public sealed partial record SetRecord : IClosedSet<SetRecord> { public static readonly SetRecord A = new(); }

            // Abstract, so a closed set it is not: refused as unsealed, never skipped.
            public abstract partial class SetAbstract : IClosedSet<SetAbstract> { }

            public sealed partial class SetWithName : IClosedSet<SetWithName> { public static readonly SetWithName A = new(); public string Name => "A"; }

            public sealed partial class SetWithExplicitAll : IClosedSet<SetWithExplicitAll> { public static readonly SetWithExplicitAll A = new(); static System.Collections.Generic.IReadOnlyList<SetWithExplicitAll> IClosedSet<SetWithExplicitAll>.All => [A]; }

            public sealed partial class SetWithParsable : IClosedSet<SetWithParsable>, System.IParsable<SetWithParsable> { public static readonly SetWithParsable A = new(); }

            [System.ComponentModel.TypeConverter(typeof(System.ComponentModel.StringConverter))]
            public sealed partial class SetWithTypeConverterAttribute : IClosedSet<SetWithTypeConverterAttribute> { public static readonly SetWithTypeConverterAttribute A = new(); }

            [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
            public sealed partial class SetWithJsonConverterAttribute : IClosedSet<SetWithJsonConverterAttribute> { public static readonly SetWithJsonConverterAttribute A = new(); }

            public sealed partial class SetWithMemberNamedParse : IClosedSet<SetWithMemberNamedParse> { public static readonly SetWithMemberNamedParse A = new(); public static readonly SetWithMemberNamedParse Parse = new(); }

            public sealed partial class SetWithMemberNamedToString : IClosedSet<SetWithMemberNamedToString> { public static readonly new SetWithMemberNamedToString ToString = new(); }

            public sealed partial class SetWithMemberNamedAll : IClosedSet<SetWithMemberNamedAll> { public static readonly SetWithMemberNamedAll All = new(); }

            public sealed partial class SetWithCasedMembers : IClosedSet<SetWithCasedMembers> { public static readonly SetWithCasedMembers Open = new(); public static readonly SetWithCasedMembers open = new(); }

            // Read as a member, it was none: its name threw and TryParse never found it.
            public sealed partial class SetWithInternalMember : IClosedSet<SetWithInternalMember> { public static readonly SetWithInternalMember A = new(); internal static readonly SetWithInternalMember Draft = new(); }

            public sealed partial class SetWithCaseVariants : IClosedSet<SetWithCaseVariants> { public static readonly SetWithCaseVariants Express = new(); public static readonly SetWithCaseVariants EXPRESS = new(); }

            public sealed partial class SetWithIgnoreCaseTryParse : IClosedSet<SetWithIgnoreCaseTryParse> { public static readonly SetWithIgnoreCaseTryParse A = new(); public static bool TryParse(string? name, bool ignoreCase, out SetWithIgnoreCaseTryParse? member) { member = A; return true; } }

            public sealed partial class SetWithIgnoreCaseParse : IClosedSet<SetWithIgnoreCaseParse> { public static readonly SetWithIgnoreCaseParse A = new(); public static SetWithIgnoreCaseParse Parse(string name, bool ignoreCase) => A; }

            // Named as a generated member: LAMA0611 (CS0542) inside the generated code.
            public static class SetNames
            {
                public sealed partial class Name : IClosedSet<Name> { public static readonly Name A = new(); }

                public sealed partial class Match : IClosedSet<Match> { public static readonly Match A = new(); }
            }
            """;

        /// <summary>
        /// Under the ignored <c>artifacts/</c> rather than the temp folder: on macOS that is behind the
        /// <c>/var</c> symlink, and NuGet's relative project paths then resolve against the wrong side.
        /// </summary>
        private readonly string _directory = Path.Combine(RepositoryRoot(), "artifacts", $"consumer-{Guid.NewGuid():N}");

        private readonly string _setsDirectory = Path.Combine(RepositoryRoot(), "artifacts", $"consumer-{Guid.NewGuid():N}");

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

        /// <summary>The errors of both builds.</summary>
        public IReadOnlyList<Diagnostic> Errors { get; private set; } = [];

        /// <summary>The errors of the build without the refused closed sets, where no compiler error hides a LAMA0611.</summary>
        public IReadOnlyList<Diagnostic> DeclarationErrors { get; private set; } = [];

        public string Output { get; private set; } = "";

        public async ValueTask InitializeAsync()
        {
            var declarations = Build(_directory, new Dictionary<string, string> { ["Declarations.cs"] = Declarations, ["OtherFile.cs"] = OtherFile });
            var sets = Build(_setsDirectory, new Dictionary<string, string> { ["RefusedSets.cs"] = RefusedSets });

            var (declarationOutput, declarationErrors) = await declarations;
            var (setOutput, setErrors) = await sets;

            DeclarationErrors = declarationErrors;
            Errors = [.. declarationErrors.Concat(setErrors).Distinct()];
            Output = declarationOutput + setOutput;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var directory in new[] { _directory, _setsDirectory })
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Builds a consumer of the generators and the analyzer in <paramref name="directory"/> from
        /// <paramref name="sources"/>, and reads its errors. A declaration whose build error is the
        /// compiler's own (CS0111) belongs in a consumer of its own: beside it, Metalama reported no error
        /// in the code it generated into a declaration's own file, which the other guards rely on.
        /// </summary>
        internal static async Task<(string Output, IReadOnlyList<Diagnostic> Errors)> Build(string directory, IReadOnlyDictionary<string, string> sources)
        {
            Directory.CreateDirectory(directory);

            var src = Path.Combine(RepositoryRoot(), "src");
            await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"),
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
            foreach (var (name, source) in sources)
                await File.WriteAllTextAsync(Path.Combine(directory, name), source);

            // Only errors are read, so the rules that ship as warnings or a suggestion are raised here.
            // CMTK0003 ships as an error, and is left to its own severity, so its reports here prove it.
            await File.WriteAllTextAsync(Path.Combine(directory, ".editorconfig"),
                """
                root = true

                [*.cs]
                dotnet_diagnostic.CMTK0005.severity = error
                dotnet_diagnostic.CMTK0006.severity = error
                dotnet_diagnostic.CMTK0007.severity = error
                dotnet_diagnostic.CMTK0008.severity = error
                dotnet_diagnostic.CMTK0009.severity = error
                """);

            // Stop MSBuild's upward search here, so the repository's own build settings (warnings as
            // errors, documentation, central package management) do not apply to the consumer.
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            // --no-dependencies and RestoreRecursive=false: the referenced projects are already built
            // by this test run, and restoring them again could rewrite restore output that other
            // tests read concurrently. Already built in this run's configuration, which is therefore
            // passed on: without it the consumer builds in Debug and, in a Release run such as CI's,
            // finds no referenced assembly at all.
            using var process = Process.Start(new ProcessStartInfo(
                "dotnet", ["build", directory, "-c", Configuration, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                Environment            = { ["DOTNET_CLI_UI_LANGUAGE"] = "en" },
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var text = await output + await errors;
            IReadOnlyList<Diagnostic> found = [.. DiagnosticLine().Matches(text).Select(match => new Diagnostic(match.Groups["id"].Value, match.Groups["message"].Value)).Distinct()];

            found.ShouldNotBeEmpty($"The consumer build reported no errors at all, so it did not run as intended:{Environment.NewLine}{text}");
            return (text, found);
        }

        /// <summary>The configuration this test assembly, and so the projects it references, was built in.</summary>
        private static string Configuration =>
            typeof(Consumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
         ?? throw new InvalidOperationException("The test assembly carries no AssemblyConfigurationAttribute.");

        internal static string RepositoryRoot()
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
