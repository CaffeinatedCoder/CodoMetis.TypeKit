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
    [InlineData("CMTK1011", "HandWrittenSpanParseOverString")]
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
    [InlineData("SetWithCaseVariants.EXPRESS", "differs from 'SetWithCaseVariants.Express' only in case, which TryParse(name, ignoreCase: true) cannot tell apart")]
    public void A_member_name_the_generators_take_is_refused_with_the_reason(string member, string reason) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1021" && error.Message.StartsWith($"'{member}' {reason}, so ") && error.Message.Contains("rename the member"), consumer.Output);

    /// <summary>
    /// What the generators implement and attach, written by hand, failed the aspect or the generated
    /// code: <c>IParsable</c> twice, an attribute twice (CS0579). The error names each. An interface's
    /// explicit implementation of <c>IClosedSet&lt;TSelf&gt;</c> was replaced by the generated member without
    /// a word, and <c>IParsable</c> declared through an interface failed the aspect (LAMA0512) (#191).
    /// </summary>
    [Theory]
    [InlineData("SetWithParsable", "the interface IParsable<SetWithParsable>")]
    [InlineData("SetWithParsableThroughInterface", "the interface IParsable<SetWithParsableThroughInterface> in its interface ISetParsesItself<SetWithParsableThroughInterface>")]
    [InlineData("SetWithNameThroughInterface", "the explicit implementation of IClosedSet<SetWithNameThroughInterface>.Name in its interface ISetNamesItself<SetWithNameThroughInterface>")]
    [InlineData("SetWithNameThroughInterface", "the explicit implementation of IClosedSet<SetWithNameThroughInterface>.TryParse(string?, out SetWithNameThroughInterface) in its interface ISetNamesItself<SetWithNameThroughInterface>")]
    [InlineData("SetOverLibraryBase", "the explicit implementation of IClosedSet<SetOverLibraryBase>.Name in its base type ExplicitSetBase<SetOverLibraryBase>")]
    [InlineData("SetWithNameThroughLibraryInterface", "the explicit implementation of IClosedSet<SetWithNameThroughLibraryInterface>.Name in its interface IProbeSetNames<SetWithNameThroughLibraryInterface>")]
    [InlineData("SetOverParsingBase", "ParsingSetBase<SetOverParsingBase>.Parse(string) in its base type ParsingSetBase<SetOverParsingBase>")]
    [InlineData("SetOverIgnoreCaseParsingBase", "IgnoreCaseParsingSetBase<SetOverIgnoreCaseParsingBase>.Parse(string, bool) in its base type IgnoreCaseParsingSetBase<SetOverIgnoreCaseParsingBase>")]
    [InlineData("SetOverTryParsingBase", "TryParsingSetBase<SetOverTryParsingBase>.TryParse(string?, bool, out SetOverTryParsingBase) in its base type TryParsingSetBase<SetOverTryParsingBase>")]
    [InlineData("SetWithTryParseByRef", "SetWithTryParseByRef.TryParse(string?, ref SetWithTryParseByRef)")]
    [InlineData("SetOverTryParsingByRefBase", "TryParsingByRefSetBase<SetOverTryParsingByRefBase>.TryParse(string?, bool, ref SetOverTryParsingByRefBase) in its base type TryParsingByRefSetBase<SetOverTryParsingByRefBase>")]
    [InlineData("SetOverMatchingBase", "MatchingSetBase.Match<TResult>(Func<TResult>) in its base type MatchingSetBase")]
    [InlineData("SetOverParsableBase", "the interface IParsable<SetOverParsableBase> in its base type ParsableSetBase<SetOverParsableBase>")]
    [InlineData("SetOverAllListingBase", "AllListingSetBase.All in its base type AllListingSetBase")]
    [InlineData("SetOverProtectedFieldBase", "ProtectedFieldSetBase._name in its base type ProtectedFieldSetBase")]
    [InlineData("SetOverQualifiedBase", "QualifiedSetBase.QualifiedName in its base type QualifiedSetBase")]
    [InlineData("SetOverOpenToStringBase", "OpenToStringSetBase.ToString() in its base type, which is not sealed, so it is not generated: its generated ToString() replaces it")]
    [InlineData("SetOverLibraryParsingBase", "LibraryParsingSetBase<SetOverLibraryParsingBase>.Parse(string) in its base type LibraryParsingSetBase<SetOverLibraryParsingBase>")]
    [InlineData("SetOverLibraryParsableBase", "the interface IParsable<SetOverLibraryParsableBase> in its base type LibraryParsableSetBase<SetOverLibraryParsableBase>")]
    [InlineData("SetOverLibraryOpenToString", "LibraryOpenToStringSetBase.ToString() in its base type, which is not sealed")]
    [InlineData("SetWithTypeConverterAttribute", "the attribute TypeConverter")]
    [InlineData("SetWithJsonConverterAttribute", "the attribute JsonConverter")]
    public void What_the_generators_implement_or_attach_is_refused_by_name_on_a_set(string type, string declared) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(declared), consumer.Output);

    /// <summary>
    /// What a base class declares that the set cannot reach, or that the generators keep, is no refusal: its
    /// <c>sealed</c> <c>ToString()</c> is the seam, as for a value object, where it failed the aspect (LAMA0502,
    /// #205); its private <c>_name</c> and <c>_all</c>, and a <c>[JsonConverter]</c> on it, which the set's own
    /// replaces, built before and still do (measured 2026-10-30).
    /// </summary>
    [Theory]
    [InlineData("SetOverSealedToStringBase")]
    [InlineData("SetOverLibrarySealedToString")]
    [InlineData("SetOverPrivateFieldsBase")]
    [InlineData("SetOverAttributedBase")]
    public void What_a_set_s_base_class_declares_out_of_the_generators_way_is_not_refused(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}"), consumer.Output);

    /// <summary>
    /// A member whose name starts with the Kelvin sign (U+212A) and one that starts with K are two names
    /// to <c>TryParse(name, ignoreCase: true)</c>, which ignores case ordinally, and one to <c>Match</c>,
    /// whose parameters lower-case the first letter: both are <c>kelvin</c>, and the set failed inside
    /// the generated code (LAMA0611, CS0100). CMTK1021 groups by each transform.
    /// </summary>
    [Fact]
    public void A_member_whose_Match_parameter_another_has_is_refused_with_the_reason()
    {
        var kelvinSign = ((char)0x212A).ToString();

        consumer.Errors.ShouldContain(error => error.Id == "CMTK1021"
                                            && error.Message.StartsWith($"'SetWithKelvinSign.{kelvinSign}elvin' gives Match the parameter name 'kelvin' that 'SetWithKelvinSign.Kelvin' gives it too, so ")
                                            && error.Message.Contains("rename the member"), consumer.Output);
    }

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
    /// A member assigned in the static constructor compiles, and the generators named it before that
    /// body ran: <c>Closed.Match</c> took the <c>open</c> branch, and <c>Parse("Open")</c> was written as
    /// <c>"Closed"</c>. CMTK0013 reports it in the project that declares the set, beside the weaving.
    /// </summary>
    [Fact]
    public void CMTK0013_reports_a_member_assigned_in_the_static_constructor() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK0013" && error.Message.StartsWith("'SetReassigned.Open' is written outside its declaration"), consumer.Output);

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
    [InlineData("TryParseByRef", "TryParseByRef.TryParse(string?, IFormatProvider?, ref TryParseByRef)")]
    [InlineData("TryParseByIn", "TryParseByIn.TryParse(string?, IFormatProvider?, in TryParseByIn)")]
    [InlineData("ParseOfOtherNullability", "ParseOfOtherNullability.Parse(string?, IFormatProvider)")]
    [InlineData("TryFormatByRef", "TryFormatByRef.TryFormat(Span<char>, ref int, ReadOnlySpan<char>, IFormatProvider?)")]
    [InlineData("HandWrittenMinValue", "HandWrittenMinValue.MinValue")]
    [InlineData("HandWrittenJsonConverter", "the attribute JsonConverter")]
    [InlineData("HandWrittenTypeConverter", "the attribute TypeConverter")]
    [InlineData("HandWrittenOrdering", "the attribute HandWrittenOrdering")]
    [InlineData("HandWrittenInterface", "the interface IValueObject<HandWrittenInterface, int>")]
    [InlineData("ExplicitCreate", "Create only as an explicit implementation of IValidatedValue<ExplicitCreate, string, Fault>")]
    [InlineData("LenientOverStrictBase", "Create both as a static method and as an explicit implementation of IValidatedValue<LenientOverStrictBase, string, LibraryFault> in its base type StrictCreateBase<LenientOverStrictBase>, so it is not generated: every generated way in calls LenientOverStrictBase.Create and generic code calling T.Create reaches the inherited one, so it would have two rule sets; declare IValidatedValue<LenientOverStrictBase, string, LibraryFault> on LenientOverStrictBase itself, so that its own Create implements it")]
    [InlineData("OnlyStrictBase", "Create only as an explicit implementation of IValidatedValue<OnlyStrictBase, string, LibraryFault> in its base type StrictCreateBase<OnlyStrictBase>, so it is not generated: the generated code calls OnlyStrictBase.Create, which cannot reach an explicit implementation; declare it as 'public static Result<OnlyStrictBase, LibraryFault> Create(string value)', and IValidatedValue<OnlyStrictBase, string, LibraryFault> on OnlyStrictBase itself")]
    [InlineData("LenientOverPublicBase", "Create both as a static method and as PublicCreateBase<LenientOverPublicBase>.Create(string) in its base type PublicCreateBase<LenientOverPublicBase>, so it is not generated: every generated way in calls LenientOverPublicBase.Create and generic code calling T.Create reaches the inherited one")]
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
    [InlineData("EqualsOnClass", "EqualsOnClass.Equals(EqualsOnClass?), so", "(declare EqualsOnClass as IValidatedValue to get one)")]
    [InlineData("EqualsDeeperBase", "EqualsDeeperBase.Equals(DeepEqualityBase?), so", "(declare EqualsDeeperBase as IValidatedValue to get one)")]
    public void A_hand_written_equality_is_refused_by_name(string type, string members, string remedy) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1011" && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(members)
                                            && error.Message.Contains("a value object's equality is its wrapped value's") && error.Message.Contains(remedy), consumer.Output);

    /// <summary>
    /// A base record's equality, in this assembly or another, was called by the derived record's
    /// synthesized one and was not checked: a base comparing by reference made two value objects
    /// holding 42 unequal while <c>CompareTo</c> answered 0 (reported 2026-10-14, there since 1.0.0).
    /// Over a base record the equality is generated now, and CMTK1023, a Warning raised to an error here,
    /// names what it no longer calls.
    /// </summary>
    [Theory]
    [InlineData("InheritsReferenceEquality", "ReferenceEqualityBase.Equals(ReferenceEqualityBase?) in its base type ReferenceEqualityBase, ReferenceEqualityBase.GetHashCode() in its base type ReferenceEqualityBase, which")]
    [InlineData("InheritsHashCode", "HashCodeBase.GetHashCode() in its base type HashCodeBase, which")]
    [InlineData("InheritsExplicitEquatable", "the explicit implementation of IEquatable<EquatableBase>.Equals(EquatableBase?) in its base type EquatableBase, which")]
    [InlineData("InheritsGenericEquality", "GenericEqualityBase<int>.Equals(GenericEqualityBase<int>?) in its base type GenericEqualityBase<int>, GenericEqualityBase<int>.GetHashCode() in its base type GenericEqualityBase<int>, which")]
    [InlineData("InheritsLibraryReferenceEquality", "ProbeReferenceEqualityRecord.Equals(ProbeReferenceEqualityRecord?) in its base type ProbeReferenceEqualityRecord, ProbeReferenceEqualityRecord.GetHashCode() in its base type ProbeReferenceEqualityRecord, which")]
    public void A_base_records_equality_is_named(string type, string members) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1023" && error.Message.StartsWith($"'{type}' inherits {members} its generated equality does not call")
                                            && error.Message.Contains("a value object's equality is its wrapped value's"), consumer.Output);

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
    /// An interface's explicit implementation of a generated interface's member is the default every type
    /// declaring the interface reaches until it declares a public member of its own, so the generated one
    /// replaced it without a word: <c>Max&lt;T&gt;()</c> read the generated <c>MaxValue</c> where the
    /// interface's threw, and an <c>IFormattable</c> call printed the value where the interface answered its
    /// own text (#191, measured 2026-10-30 for each interface here). It is refused as the value object's own
    /// is, naming the interface, two interfaces down and through a base record too. <c>IConvertible</c>
    /// declared through an interface failed the aspect (LAMA0041), as declaring it does.
    /// </summary>
    [Theory]
    [InlineData("CMTK1011", "FormatsThroughInterface", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its interface IFormatsItself")]
    [InlineData("CMTK1011", "SpanFormatsThroughInterface", "the explicit implementation of ISpanFormattable.TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?) in its interface ISpanFormatsItself")]
    [InlineData("CMTK1011", "Utf8FormatsThroughInterface", "the explicit implementation of IUtf8SpanFormattable.TryFormat(Span<byte>, out int, ReadOnlySpan<char>, IFormatProvider?) in its interface IUtf8FormatsItself")]
    [InlineData("CMTK1011", "ParsesThroughInterface", "the explicit implementation of IParsable<ParsesThroughInterface>.Parse(string, IFormatProvider?) in its interface IParsesItself<ParsesThroughInterface>")]
    [InlineData("CMTK1011", "SpanParsesThroughInterface", "the explicit implementation of ISpanParsable<SpanParsesThroughInterface>.Parse(ReadOnlySpan<char>, IFormatProvider?) in its interface ISpanParsesItself<SpanParsesThroughInterface>")]
    [InlineData("CMTK1011", "Utf8ParsesThroughInterface", "the explicit implementation of IUtf8SpanParsable<Utf8ParsesThroughInterface>.Parse(ReadOnlySpan<byte>, IFormatProvider?) in its interface IUtf8ParsesItself<Utf8ParsesThroughInterface>")]
    [InlineData("CMTK1011", "BoundedThroughInterface", "the explicit implementation of IMinMaxValue<BoundedThroughInterface>.MinValue in its interface IBoundedItself<BoundedThroughInterface>")]
    [InlineData("CMTK1011", "ConvertsThroughInterface", "the interface IConvertible in its interface IConvertsItself")]
    [InlineData("CMTK1008", "OrdersThroughInterface", "the explicit implementation of IComparable.CompareTo(object?) in its interface IOrdersItself")]
    [InlineData("CMTK1008", "OrdersThroughGenericInterface", "the explicit implementation of IComparable<OrdersThroughGenericInterface>.CompareTo(OrdersThroughGenericInterface) in its interface IOrdersItselfAs<OrdersThroughGenericInterface>")]
    [InlineData("CMTK1008", "ComparesThroughInterface", "the explicit implementation of IComparisonOperators<ComparesThroughInterface, ComparesThroughInterface, bool>.operator <(ComparesThroughInterface, ComparesThroughInterface) in its interface IComparesItself<ComparesThroughInterface>")]
    [InlineData("CMTK1011", "FormatsThroughInterfaceOnClass", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its interface IFormatsItself")]
    [InlineData("CMTK1008", "OrdersThroughGenericInterfaceOnClass", "the explicit implementation of IComparable<OrdersThroughGenericInterfaceOnClass>.CompareTo(OrdersThroughGenericInterfaceOnClass?) in its interface IOrdersItselfAs<OrdersThroughGenericInterfaceOnClass>")]
    [InlineData("CMTK1011", "FormatsTwoInterfacesDown","the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its interface IFormatsItself")]
    [InlineData("CMTK1011", "ConvertsTwoInterfacesDown", "the interface IConvertible in its interface IConvertsTwoDown")]
    [InlineData("CMTK1011", "FormatsThroughBaseInterface", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its interface IFormatsItself")]
    [InlineData("CMTK1011", "BoundedThroughBaseInterface", "the explicit implementation of IMinMaxValue<BoundedThroughBaseInterface>.MinValue in its interface IBoundedItself<BoundedThroughBaseInterface>")]
    [InlineData("CMTK1008", "OrdersThroughBaseInterface", "the explicit implementation of IComparable.CompareTo(object?) in its interface IOrdersItself")]
    [InlineData("CMTK1011", "ConvertsThroughBaseInterface", "the interface IConvertible in its base type ConvertsItselfBase")]
    [InlineData("CMTK1011", "ContractThroughInterface", "the interface IValueObject<ContractThroughInterface, Guid> in its interface IEntityIdentifier<ContractThroughInterface>")]
    [InlineData("CMTK1011", "ContractTwoInterfacesDown", "the interface IValueObject<ContractTwoInterfacesDown, Guid> in its interface IAggregateIdentifier<ContractTwoInterfacesDown>")]
    [InlineData("CMTK1011", "MaterializerThroughInterface", "the interface IValueObjectMaterializer<MaterializerThroughInterface, int> in its interface IReadsBackItself<MaterializerThroughInterface>")]
    [InlineData("CMTK1011", "PlainContractThroughInterface", "the interface IPlainValueObject<PlainContractThroughInterface, int> in its interface IFromableItself<PlainContractThroughInterface>")]
    [InlineData("CMTK1011", "ContractThroughBaseInterface", "the interface IValueObject<ContractThroughBaseInterface, Guid> in its base type EntityIdentifierBase<ContractThroughBaseInterface>")]
    [InlineData("CMTK1011", "ContractThroughLibraryInterface", "the interface IValueObject<ContractThroughLibraryInterface, Guid> in its interface ILibraryEntityIdentifier<ContractThroughLibraryInterface>")]
    [InlineData("CMTK1011", "FormatsThroughLibraryInterface", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its interface IProbeFormats")]
    [InlineData("CMTK1011", "BoundedThroughLibraryInterface", "the explicit implementation of IMinMaxValue<BoundedThroughLibraryInterface>.MinValue in its interface IProbeBounded<BoundedThroughLibraryInterface>")]
    [InlineData("CMTK1008", "OrdersThroughLibraryInterface", "the explicit implementation of IComparable.CompareTo(object?) in its interface IProbeOrders")]
    public void An_interface_s_explicit_implementation_of_a_generated_interface_is_refused_by_name(string id, string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(member), consumer.Output);

    /// <summary>
    /// An interface's default the generators do not replace is no refusal: beside <c>ToString()</c>, the seam,
    /// no formatting interface is generated, and a <c>Uri</c> has no ordering. The record's own <c>==</c>
    /// replaces an interface's in every record, generated or not (measured 2026-10-30). .NET's
    /// <c>INumberBase&lt;TSelf&gt;</c> implements <c>IUtf8SpanFormattable</c> and <c>IUtf8SpanParsable</c>
    /// explicitly as bridges to the UTF-16 members, which every numeric type of .NET replaces with its own, as
    /// the generated ones do: refused, no value object could implement it.
    /// </summary>
    [Theory]
    [InlineData("FormatsThroughInterfaceWithToString")]
    [InlineData("OrdersThroughInterfaceOverUri")]
    [InlineData("EqualsThroughInterface")]
    [InlineData("MetersAsNumber")]
    public void An_interface_s_default_the_generators_do_not_replace_is_not_refused(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}'"), consumer.Output);

    /// <summary>
    /// One <c>Create</c> that both the generated code and generic <c>T.Create</c> reach builds: the value object's
    /// own, where it declares the marker again over a base that implements <c>Create</c> explicitly, and a base's
    /// public one alone (#195).
    /// </summary>
    [Theory]
    [InlineData("LenientListedOverStrictBase")]
    [InlineData("OnlyPublicBase")]
    public void One_Create_over_a_base_record_is_not_refused(string type) =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains($"'{type}'"), consumer.Output);

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
    [InlineData("CMTK1011", "InheritsLibraryFormattable", "the explicit implementation of IFormattable.ToString(string?, IFormatProvider?) in its base type ProbeFormattableRecord")]
    [InlineData("CMTK1011", "InheritsLibraryMinMax", "the explicit implementation of IMinMaxValue<InheritsLibraryMinMax>.MinValue in its base type ProbeMinMaxRecord<InheritsLibraryMinMax>")]
    [InlineData("CMTK1011", "InheritsPublicMinMax", "PublicMinMaxItselfBase<InheritsPublicMinMax>.MinValue in its base type PublicMinMaxItselfBase<InheritsPublicMinMax>")]
    [InlineData("CMTK1011", "InheritsPublicFormatting", "PublicFormattingBase.ToString(string?, IFormatProvider?) in its base type PublicFormattingBase")]
    [InlineData("CMTK1008", "InheritsPublicObjectOrder", "PublicObjectOrderBase.CompareTo(object?) in its base type PublicObjectOrderBase")]
    [InlineData("CMTK1011", "InheritsPublicParsing", "PublicParsingBase<InheritsPublicParsing>.Parse(string, IFormatProvider?) in its base type PublicParsingBase<InheritsPublicParsing>")]
    [InlineData("CMTK1011", "InheritsTryParseByRef", "TryParseByRefBase<InheritsTryParseByRef>.TryParse(string?, IFormatProvider?, ref InheritsTryParseByRef) in its base type TryParseByRefBase<InheritsTryParseByRef>")]
    [InlineData("CMTK1011", "InheritsMinMaxNames", "MinMaxNamesBase<InheritsMinMaxNames>.MinValue in its base type MinMaxNamesBase<InheritsMinMaxNames>")]
    [InlineData("CMTK1011", "InheritsValueName", "ValueNameBase.Value in its base type ValueNameBase")]
    [InlineData("CMTK1011", "InheritsFromName", "FromNameBase<InheritsFromName>.From(int) in its base type FromNameBase<InheritsFromName>")]
    [InlineData("CMTK1011", "InheritsTryFormatName", "TryFormatNameBase.TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider?) in its base type TryFormatNameBase")]
    [InlineData("CMTK1008", "InheritsCompareTo", "CompareToSeamBase<InheritsCompareTo>.CompareTo(InheritsCompareTo?) in its base type CompareToSeamBase<InheritsCompareTo>, but its comparison is generated from CompareTo(InheritsCompareTo)")]
    [InlineData("CMTK1008", "InheritsObjectCompareTo", "ObjectCompareToBase.CompareTo(object?) in its base type ObjectCompareToBase")]
    [InlineData("CMTK1011", "InheritsTryFrom", "TryFromSeamBase<InheritsTryFrom>.TryFrom(string) in its base type TryFromSeamBase<InheritsTryFrom>, so it is not generated: a seam is kept only where the value object declares it")]
    [InlineData("CMTK1011", "InheritsRevalidate", "RevalidateSeamBase<InheritsRevalidate>.Revalidate() in its base type RevalidateSeamBase<InheritsRevalidate>, so it is not generated: a seam is kept only where the value object declares it")]
    [InlineData("CMTK1011", "InheritsLibraryPublicMinMax", "PublicMinMaxBase<InheritsLibraryPublicMinMax>.MinValue in its base type PublicMinMaxBase<InheritsLibraryPublicMinMax>")]
    public void What_a_base_record_declares_is_refused_by_name(string id, string type, string member) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.StartsWith($"'{type}' declares ") && error.Message.Contains(member), consumer.Output);

    /// <summary>
    /// An interface derived from a marker is a way to find a value object, not a contract the generators
    /// implement, so it is generated as usual (#192).
    /// </summary>
    [Fact]
    public void A_marker_declared_through_an_interface_is_generated() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'ThroughMarkerInterface'") || error.Message.Contains("IIdentifierMarker"), consumer.Output);

    /// <summary>
    /// A base record's public <c>CompareTo(object)</c> that implements <c>IComparable</c> is found twice, by the
    /// interface mapping and as an inherited comparison member (#194), and named once.
    /// </summary>
    [Fact]
    public void A_base_member_found_twice_is_named_once() =>
        consumer.Errors.Where(error => error.Message.StartsWith("'InheritsPublicObjectOrder' declares ", StringComparison.Ordinal))
                .ShouldHaveSingleItem(consumer.Output)
                .Message.Split("PublicObjectOrderBase.CompareTo(object?)").Length.ShouldBe(2, consumer.Output);

    /// <summary>
    /// A public static member of another project that implements a static abstract one reads as an explicit
    /// implementation there, since the compiler records every such implementation as one, and the error called it
    /// "the explicit implementation of IMinMaxValue..." (#197). Metalama displays it as
    /// <c>PublicMinMaxBase&lt;T&gt;.IMinMaxValue.MinValue</c>, so it is named once, by its type and the member.
    /// </summary>
    [Fact]
    public void A_public_member_of_another_project_is_named_once_as_public()
    {
        var declared = consumer.Errors.Where(error => error.Message.StartsWith("'InheritsLibraryPublicMinMax' declares ", StringComparison.Ordinal))
                               .ShouldHaveSingleItem(consumer.Output)
                               .Message.Split(", so it is not generated")[0]["'InheritsLibraryPublicMinMax' declares ".Length..]
                               .Split(", ");

        declared.Order(StringComparer.Ordinal).ShouldBe(
            ["PublicMinMaxBase<InheritsLibraryPublicMinMax>.MaxValue in its base type PublicMinMaxBase<InheritsLibraryPublicMinMax>",
             "PublicMinMaxBase<InheritsLibraryPublicMinMax>.MinValue in its base type PublicMinMaxBase<InheritsLibraryPublicMinMax>"], ignoreOrder: false, customMessage: consumer.Output);
    }

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
    /// failed inside the generated code (LAMA0611, CS1503 and CS0122). A base record's
    /// <c>FromKnownGood(T)</c> is hidden by the generated one, so it is no inherited seam (#194).
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
    [InlineData("InheritsFromKnownGoodName", "FromKnownGoodNameBase")]
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
                .ShouldBe(["CMTK0001", "CMTK0003", "CMTK0004", "CMTK0005", "CMTK0006", "CMTK0007", "CMTK0008", "CMTK0009", "CMTK0012", "CMTK0013", "CMTK1000", "CMTK1001", "CMTK1002", "CMTK1003", "CMTK1004", "CMTK1005", "CMTK1006", "CMTK1007", "CMTK1008", "CMTK1009", "CMTK1010", "CMTK1011", "CMTK1012", "CMTK1013", "CMTK1014", "CMTK1015", "CMTK1016", "CMTK1017", "CMTK1018", "CMTK1019", "CMTK1020", "CMTK1021", "CMTK1022", "CMTK1023", "CS0535", "CS0738"], ignoreOrder: false, customMessage: consumer.Output);

    /// <summary>
    /// CS0535 is allowed above only for a refused closed set, whose interface members the generators
    /// did not implement, and CS0738 where a base class's member of a generated name has another type
    /// (a static <c>int All</c>, #205). A value object or a generated set that lost a member the same way is a
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
        consumer.Errors.Where(error => error.Id is "CS0535" or "CS0738")
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

        errors.Select(error => error.Id).Distinct().Count().ShouldBe(24, $"Not every CMTK1000-CMTK1023 diagnostic was observed, so the check below is incomplete:{Environment.NewLine}{consumer.Output}");
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

            // Generated, and named before the static constructor's body, which then swaps a member: CMTK0013's.
            public sealed partial class SetReassigned : IClosedSet<SetReassigned>
            {
                public static readonly SetReassigned Open = new();
                public static readonly SetReassigned Closed = new();

                static SetReassigned() { Open = Closed; }
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

            // string is ISpanParsable, so the span Parse is generated over it too, and a hand-written one is refused.
            public readonly partial record struct HandWrittenSpanParseOverString : IValue<string>
            {
                public static HandWrittenSpanParseOverString Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct HandWrittenFormat : IValue<int>
            {
                public string ToString(string? format, System.IFormatProvider? provider) => "";
            }

            // No overload in C#: ref or in against the generated out (CS0663), and another nullability (#226).
            public readonly partial record struct TryParseByRef : IValue<int>
            {
                public static bool TryParse(string? s, System.IFormatProvider? provider, ref TryParseByRef result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct TryParseByIn : IValue<int>
            {
                public static bool TryParse(string? s, System.IFormatProvider? provider, in TryParseByIn result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct ParseOfOtherNullability : IValue<int>
            {
                public static ParseOfOtherNullability Parse(string? s, System.IFormatProvider provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct TryFormatByRef : IValue<int>
            {
                public bool TryFormat(System.Span<char> destination, ref int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => false;
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

            // An interface's explicit implementation of a generated interface's member: the default every
            // type declaring the interface reaches until it declares a public member of its own, which the
            // generated one replaced without a word (#191). Each interface implements every member of the
            // generated one, so a refused value object lacks none (CS0535).
            public interface IFormatsItself : System.IFormattable
            {
                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public readonly partial record struct FormatsThroughInterface : IValue<int>, IFormatsItself;

            public interface ISpanFormatsItself : System.ISpanFormattable
            {
                string System.IFormattable.ToString(string? format, System.IFormatProvider? formatProvider) => "***";

                bool System.ISpanFormattable.TryFormat(System.Span<char> destination, out int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct SpanFormatsThroughInterface : IValue<long>, ISpanFormatsItself;

            public interface IUtf8FormatsItself : System.IUtf8SpanFormattable
            {
                bool System.IUtf8SpanFormattable.TryFormat(System.Span<byte> utf8Destination, out int bytesWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public readonly partial record struct Utf8FormatsThroughInterface : IValue<int>, IUtf8FormatsItself;

            public interface IParsesItself<TSelf> : System.IParsable<TSelf> where TSelf : IParsesItself<TSelf>
            {
                static TSelf System.IParsable<TSelf>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IParsable<TSelf>.TryParse(string? s, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct ParsesThroughInterface : IValidatedValue<ParsesThroughInterface, string, Fault>, IParsesItself<ParsesThroughInterface>
            {
                public static Result<ParsesThroughInterface, Fault> Create(string value) => Result.Error(Fault.Refused);
            }

            public interface ISpanParsesItself<TSelf> : System.ISpanParsable<TSelf> where TSelf : ISpanParsesItself<TSelf>
            {
                static TSelf System.IParsable<TSelf>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IParsable<TSelf>.TryParse(string? s, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();

                static TSelf System.ISpanParsable<TSelf>.Parse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.ISpanParsable<TSelf>.TryParse(System.ReadOnlySpan<char> s, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct SpanParsesThroughInterface : IValue<int>, ISpanParsesItself<SpanParsesThroughInterface>;

            public interface IUtf8ParsesItself<TSelf> : System.IUtf8SpanParsable<TSelf> where TSelf : IUtf8ParsesItself<TSelf>
            {
                static TSelf System.IUtf8SpanParsable<TSelf>.Parse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IUtf8SpanParsable<TSelf>.TryParse(System.ReadOnlySpan<byte> utf8Text, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            }

            public readonly partial record struct Utf8ParsesThroughInterface : IValue<int>, IUtf8ParsesItself<Utf8ParsesThroughInterface>;

            public interface IBoundedItself<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : IBoundedItself<TSelf>
            {
                static TSelf System.Numerics.IMinMaxValue<TSelf>.MinValue => throw new System.NotSupportedException();

                static TSelf System.Numerics.IMinMaxValue<TSelf>.MaxValue => throw new System.NotSupportedException();
            }

            public readonly partial record struct BoundedThroughInterface : IValue<int>, IBoundedItself<BoundedThroughInterface>;

            public interface IOrdersItself : System.IComparable
            {
                int System.IComparable.CompareTo(object? obj) => 0;
            }

            public readonly partial record struct OrdersThroughInterface : IValue<int>, IOrdersItself;

            public interface IOrdersItselfAs<TSelf> : System.IComparable<TSelf>
            {
                int System.IComparable<TSelf>.CompareTo(TSelf? other) => 0;
            }

            public readonly partial record struct OrdersThroughGenericInterface : IValue<int>, IOrdersItselfAs<OrdersThroughGenericInterface>;

            public interface IComparesItself<TSelf> : System.Numerics.IComparisonOperators<TSelf, TSelf, bool> where TSelf : IComparesItself<TSelf>
            {
                static bool System.Numerics.IComparisonOperators<TSelf, TSelf, bool>.operator <(TSelf left, TSelf right) => false;

                static bool System.Numerics.IComparisonOperators<TSelf, TSelf, bool>.operator >(TSelf left, TSelf right) => false;

                static bool System.Numerics.IComparisonOperators<TSelf, TSelf, bool>.operator <=(TSelf left, TSelf right) => false;

                static bool System.Numerics.IComparisonOperators<TSelf, TSelf, bool>.operator >=(TSelf left, TSelf right) => false;
            }

            public readonly partial record struct ComparesThroughInterface : IValue<int>, IComparesItself<ComparesThroughInterface>;

            // IConvertible declared through an interface failed the aspect (LAMA0041), as declaring it does.
            public interface IConvertsItself : System.IConvertible
            {
                System.TypeCode System.IConvertible.GetTypeCode() => System.TypeCode.Int32;
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

            public readonly partial record struct ConvertsThroughInterface : IValue<int>, IConvertsItself;

            // A record class's, over a reference type.
            public sealed partial record FormatsThroughInterfaceOnClass : IValue<string>, IFormatsItself;

            public sealed partial record OrdersThroughGenericInterfaceOnClass : IValue<string>, IOrdersItselfAs<OrdersThroughGenericInterfaceOnClass>;

            // Two interfaces down, and through an interface of a base record.
            public interface IFormatsTwoDown : IFormatsItself;

            public readonly partial record struct FormatsTwoInterfacesDown : IValue<int>, IFormatsTwoDown;

            public interface IConvertsTwoDown : IConvertsItself;

            public readonly partial record struct ConvertsTwoInterfacesDown : IValue<int>, IConvertsTwoDown;

            public abstract record FormatsItselfBase : IFormatsItself;

            public sealed partial record FormatsThroughBaseInterface : FormatsItselfBase, IValue<int>;

            public abstract record BoundedItselfBase<TSelf> : IBoundedItself<TSelf> where TSelf : BoundedItselfBase<TSelf>, IBoundedItself<TSelf>;

            public sealed partial record BoundedThroughBaseInterface : BoundedItselfBase<BoundedThroughBaseInterface>, IValue<int>;

            public abstract record OrdersItselfBase : IOrdersItself;

            public sealed partial record OrdersThroughBaseInterface : OrdersItselfBase, IValue<int>;

            public abstract record ConvertsItselfBase : IConvertsItself;

            public sealed partial record ConvertsThroughBaseInterface : ConvertsItselfBase, IValue<int>;

            // A contract the generators implement, declared through an interface of the user's (#192): the
            // aspect failed (LAMA0512, LAMA0041), naming nothing the user wrote. Each interface implements the
            // contract's members, so a refused value object lacks none (CS0535).
            public interface IEntityIdentifier<TSelf> : IValueObject<TSelf, System.Guid> where TSelf : IEntityIdentifier<TSelf>
            {
                System.Guid IValueObject<TSelf, System.Guid>.Value => System.Guid.Empty;

                static bool IValueObject<TSelf, System.Guid>.operator ==(TSelf left, TSelf right) => false;

                static bool IValueObject<TSelf, System.Guid>.operator !=(TSelf left, TSelf right) => true;
            }

            public readonly partial record struct ContractThroughInterface : IValue<System.Guid>, IEntityIdentifier<ContractThroughInterface>;

            public interface IAggregateIdentifier<TSelf> : IEntityIdentifier<TSelf> where TSelf : IAggregateIdentifier<TSelf>;

            public readonly partial record struct ContractTwoInterfacesDown : IValue<System.Guid>, IAggregateIdentifier<ContractTwoInterfacesDown>;

            public interface IReadsBackItself<TSelf> : IValueObjectMaterializer<TSelf, int> where TSelf : IReadsBackItself<TSelf>
            {
                static TSelf IValueObjectMaterializer<TSelf, int>.Materialize(int value) => throw new System.NotSupportedException();
            }

            public readonly partial record struct MaterializerThroughInterface : IValue<int>, IReadsBackItself<MaterializerThroughInterface>;

            public interface IFromableItself<TSelf> : IPlainValueObject<TSelf, int>, IValueObject<TSelf, int> where TSelf : IFromableItself<TSelf>
            {
                static TSelf IPlainValueObject<TSelf, int>.From(int value) => throw new System.NotSupportedException();

                int IValueObject<TSelf, int>.Value => 0;
            }

            public readonly partial record struct PlainContractThroughInterface : IValue<int>, IFromableItself<PlainContractThroughInterface>;

            public abstract record EntityIdentifierBase<TSelf> : IEntityIdentifier<TSelf> where TSelf : EntityIdentifierBase<TSelf>;

            public sealed partial record ContractThroughBaseInterface : EntityIdentifierBase<ContractThroughBaseInterface>, IValue<System.Guid>;

            public readonly partial record struct ContractThroughLibraryInterface : IValue<System.Guid>, Library.ILibraryEntityIdentifier<ContractThroughLibraryInterface>;

            // An interface derived from the marker is how a value object may be found: generated as usual.
            public interface IIdentifierMarker : IValue<System.Guid>;

            public readonly partial record struct ThroughMarkerInterface : IIdentifierMarker;

            // An interface's and a base record's of another assembly, where Metalama lists no private
            // member, which an explicit implementation is: replaced or kept without a word (#191).
            public readonly partial record struct FormatsThroughLibraryInterface : IValue<int>, CodoMetis.TypeKit.Generators.Probes.IProbeFormats;

            public readonly partial record struct BoundedThroughLibraryInterface : IValue<int>, CodoMetis.TypeKit.Generators.Probes.IProbeBounded<BoundedThroughLibraryInterface>;

            public readonly partial record struct OrdersThroughLibraryInterface : IValue<int>, CodoMetis.TypeKit.Generators.Probes.IProbeOrders;

            public sealed partial record InheritsLibraryFormattable : CodoMetis.TypeKit.Generators.Probes.ProbeFormattableRecord, IValue<int>;

            public sealed partial record InheritsLibraryMinMax : CodoMetis.TypeKit.Generators.Probes.ProbeMinMaxRecord<InheritsLibraryMinMax>, IValue<int>;

            // A base record's public implementation of a generated member, or a member of a generated name
            // (#197): its static MinValue and MaxValue were the value object's, and no MinValue or MaxValue was
            // generated; the others failed the aspect (LAMA0500). Every body throws, so nothing generated is touched.
            public abstract record PublicMinMaxItselfBase<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : PublicMinMaxItselfBase<TSelf>, System.Numerics.IMinMaxValue<TSelf>
            {
                public static TSelf MinValue => throw new System.NotSupportedException();

                public static TSelf MaxValue => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsPublicMinMax : PublicMinMaxItselfBase<InheritsPublicMinMax>, IValue<int>;

            public abstract record PublicFormattingBase : System.IFormattable
            {
                public string ToString(string? format, System.IFormatProvider? formatProvider) => "***";
            }

            public sealed partial record InheritsPublicFormatting : PublicFormattingBase, IValue<int>;

            public abstract record PublicObjectOrderBase : System.IComparable
            {
                public int CompareTo(object? obj) => 0;
            }

            public sealed partial record InheritsPublicObjectOrder : PublicObjectOrderBase, IValue<int>;

            public abstract record PublicParsingBase<TSelf> : System.IParsable<TSelf> where TSelf : PublicParsingBase<TSelf>, System.IParsable<TSelf>
            {
                public static TSelf Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                public static bool TryParse(string? s, System.IFormatProvider? provider, out TSelf result) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsPublicParsing : PublicParsingBase<InheritsPublicParsing>, IValue<int>;

            public abstract record TryParseByRefBase<TSelf>
            {
                public static bool TryParse(string? s, System.IFormatProvider? provider, ref TSelf result) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsTryParseByRef : TryParseByRefBase<InheritsTryParseByRef>, IValue<int>;

            public abstract record MinMaxNamesBase<TSelf> where TSelf : MinMaxNamesBase<TSelf>
            {
                public static TSelf MinValue => throw new System.NotSupportedException();

                public static TSelf MaxValue => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsMinMaxNames : MinMaxNamesBase<InheritsMinMaxNames>, IValue<int>;

            public abstract record ValueNameBase
            {
                public int Value => 0;
            }

            public sealed partial record InheritsValueName : ValueNameBase, IValue<int>;

            public abstract record FromNameBase<TSelf> where TSelf : FromNameBase<TSelf>
            {
                public static TSelf From(int value) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsFromName : FromNameBase<InheritsFromName>, IValue<int>;

            public abstract record TryFormatNameBase
            {
                public bool TryFormat(System.Span<char> destination, out int charsWritten, System.ReadOnlySpan<char> format, System.IFormatProvider? provider) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsTryFormatName : TryFormatNameBase, IValue<long>;

            // A seam a base record declares rather than the value object (#194): its CompareTo(TSelf) left the
            // ordering unmarked for EF Core and made < overflow the stack, its TryFrom and Revalidate stood in for
            // the generated ones, and its CompareTo(object) failed the aspect (LAMA0500).
            public abstract record CompareToSeamBase<TSelf> where TSelf : CompareToSeamBase<TSelf>
            {
                public int CompareTo(TSelf? other) => 0;
            }

            public sealed partial record InheritsCompareTo : CompareToSeamBase<InheritsCompareTo>, IValue<int>;

            public abstract record ObjectCompareToBase
            {
                public int CompareTo(object? obj) => 0;
            }

            public sealed partial record InheritsObjectCompareTo : ObjectCompareToBase, IValue<int>;

            public abstract record TryFromSeamBase<TSelf> where TSelf : TryFromSeamBase<TSelf>
            {
                public static Option<TSelf> TryFrom(string value) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsTryFrom : TryFromSeamBase<InheritsTryFrom>, IValidatedValue<InheritsTryFrom, string, Fault>
            {
                public static Result<InheritsTryFrom, Fault> Create(string value) => Result.Error(Fault.Refused);
            }

            public abstract record RevalidateSeamBase<TSelf> where TSelf : RevalidateSeamBase<TSelf>
            {
                public Result<TSelf, Fault> Revalidate() => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsRevalidate : RevalidateSeamBase<InheritsRevalidate>, IValidatedValue<InheritsRevalidate, string, Fault>
            {
                public static Result<InheritsRevalidate, Fault> Create(string value) => Result.Error(Fault.Refused);
            }

            // A base's FromKnownGood(T) is another signature than the generated one, which takes the caller's
            // expression too and hides it, so it stands in for nothing. Generated as usual.
            public abstract record FromKnownGoodNameBase<TSelf> where TSelf : FromKnownGoodNameBase<TSelf>
            {
                public static TSelf FromKnownGood(string value) => throw new System.NotSupportedException();
            }

            public sealed partial record InheritsFromKnownGoodName : FromKnownGoodNameBase<InheritsFromKnownGoodName>, IValidatedValue<InheritsFromKnownGoodName, string, Fault>
            {
                public static Result<InheritsFromKnownGoodName, Fault> Create(string value) => new InheritsFromKnownGoodName(value);
            }

            // From a project the generators do not run in: read as an explicit implementation there, named as public.
            public sealed partial record InheritsLibraryPublicMinMax : Library.PublicMinMaxBase<InheritsLibraryPublicMinMax>, IValue<int>;

            // Create in a base record of another project (#195): generic code calling T.Create reached the
            // base's, which refused what the value object's own accepted.
            public sealed partial record LenientOverStrictBase : Library.StrictCreateBase<LenientOverStrictBase>
            {
                public static Result<LenientOverStrictBase, Library.LibraryFault> Create(string value) => Result.Error(Library.LibraryFault.Refused);
            }

            public sealed partial record OnlyStrictBase : Library.StrictCreateBase<OnlyStrictBase>;

            public sealed partial record LenientOverPublicBase : Library.PublicCreateBase<LenientOverPublicBase>
            {
                public static Result<LenientOverPublicBase, Library.LibraryFault> Create(string value) => Result.Error(Library.LibraryFault.Refused);
            }

            // One rule set each: the marker declared again, so the value object's own Create implements it, and a
            // base's public Create alone, which the generated code calls too.
            public sealed partial record LenientListedOverStrictBase : Library.StrictCreateBase<LenientListedOverStrictBase>, IValidatedValue<LenientListedOverStrictBase, string, Library.LibraryFault>
            {
                public static Result<LenientListedOverStrictBase, Library.LibraryFault> Create(string value) => new LenientListedOverStrictBase(value);
            }

            public sealed partial record OnlyPublicBase : Library.PublicCreateBase<OnlyPublicBase>;

            // An interface's default the generators do not replace. Beside ToString(), the seam, no
            // formatting interface is generated, and a Uri has no ordering. The record's own == replaces an
            // interface's in every record, generated or not. INumberBase<TSelf>, .NET's, implements
            // IUtf8SpanFormattable and IUtf8SpanParsable explicitly as bridges to the UTF-16 members, which
            // every numeric type of .NET replaces with its own, as the generated ones do.
            public readonly partial record struct FormatsThroughInterfaceWithToString : IValue<int>, IFormatsItself
            {
                public override string ToString() => "***";
            }

            public sealed partial record OrdersThroughInterfaceOverUri : IValue<System.Uri>, IOrdersItselfAs<OrdersThroughInterfaceOverUri>;

            public interface IEqualsItself<TSelf> : System.Numerics.IEqualityOperators<TSelf, TSelf, bool> where TSelf : IEqualsItself<TSelf>
            {
                static bool System.Numerics.IEqualityOperators<TSelf, TSelf, bool>.operator ==(TSelf? left, TSelf? right) => true;

                static bool System.Numerics.IEqualityOperators<TSelf, TSelf, bool>.operator !=(TSelf? left, TSelf? right) => false;
            }

            public readonly partial record struct EqualsThroughInterface : IValue<int>, IEqualsItself<EqualsThroughInterface>;

            public readonly partial record struct MetersAsNumber : IValue<double>, System.Numerics.INumberBase<MetersAsNumber>
            {
                public static MetersAsNumber One => throw new System.NotSupportedException();
                public static int Radix => 2;
                public static MetersAsNumber Zero => throw new System.NotSupportedException();
                public static MetersAsNumber AdditiveIdentity => throw new System.NotSupportedException();
                public static MetersAsNumber MultiplicativeIdentity => throw new System.NotSupportedException();
                public static MetersAsNumber Abs(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsCanonical(MetersAsNumber value) => true;
                public static bool IsComplexNumber(MetersAsNumber value) => false;
                public static bool IsEvenInteger(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsFinite(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsImaginaryNumber(MetersAsNumber value) => false;
                public static bool IsInfinity(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsInteger(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsNaN(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsNegative(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsNegativeInfinity(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsNormal(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsOddInteger(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsPositive(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsPositiveInfinity(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsRealNumber(MetersAsNumber value) => true;
                public static bool IsSubnormal(MetersAsNumber value) => throw new System.NotSupportedException();
                public static bool IsZero(MetersAsNumber value) => throw new System.NotSupportedException();
                public static MetersAsNumber MaxMagnitude(MetersAsNumber x, MetersAsNumber y) => throw new System.NotSupportedException();
                public static MetersAsNumber MaxMagnitudeNumber(MetersAsNumber x, MetersAsNumber y) => throw new System.NotSupportedException();
                public static MetersAsNumber MinMagnitude(MetersAsNumber x, MetersAsNumber y) => throw new System.NotSupportedException();
                public static MetersAsNumber MinMagnitudeNumber(MetersAsNumber x, MetersAsNumber y) => throw new System.NotSupportedException();
                public static MetersAsNumber Parse(System.ReadOnlySpan<char> s, System.Globalization.NumberStyles style, System.IFormatProvider? provider) => throw new System.NotSupportedException();
                public static MetersAsNumber Parse(string s, System.Globalization.NumberStyles style, System.IFormatProvider? provider) => throw new System.NotSupportedException();
                public static bool TryParse(System.ReadOnlySpan<char> s, System.Globalization.NumberStyles style, System.IFormatProvider? provider, out MetersAsNumber result) => throw new System.NotSupportedException();
                public static bool TryParse(string? s, System.Globalization.NumberStyles style, System.IFormatProvider? provider, out MetersAsNumber result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertFromChecked<TOther>(TOther value, out MetersAsNumber result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertFromSaturating<TOther>(TOther value, out MetersAsNumber result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertFromTruncating<TOther>(TOther value, out MetersAsNumber result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertToChecked<TOther>(MetersAsNumber value, out TOther result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertToSaturating<TOther>(MetersAsNumber value, out TOther result) => throw new System.NotSupportedException();
                static bool System.Numerics.INumberBase<MetersAsNumber>.TryConvertToTruncating<TOther>(MetersAsNumber value, out TOther result) => throw new System.NotSupportedException();
                public static MetersAsNumber operator +(MetersAsNumber left, MetersAsNumber right) => throw new System.NotSupportedException();
                public static MetersAsNumber operator -(MetersAsNumber left, MetersAsNumber right) => throw new System.NotSupportedException();
                public static MetersAsNumber operator *(MetersAsNumber left, MetersAsNumber right) => throw new System.NotSupportedException();
                public static MetersAsNumber operator /(MetersAsNumber left, MetersAsNumber right) => throw new System.NotSupportedException();
                public static MetersAsNumber operator +(MetersAsNumber value) => throw new System.NotSupportedException();
                public static MetersAsNumber operator -(MetersAsNumber value) => throw new System.NotSupportedException();
                public static MetersAsNumber operator ++(MetersAsNumber value) => throw new System.NotSupportedException();
                public static MetersAsNumber operator --(MetersAsNumber value) => throw new System.NotSupportedException();
            }

            // A base record's equality, which the derived record's synthesized one calls.
            public abstract record ReferenceEqualityBase
            {
                public virtual bool Equals(ReferenceEqualityBase? other) => ReferenceEquals(this, other);

                public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
            }

            public sealed partial record InheritsReferenceEquality : ReferenceEqualityBase, IValue<int>;

            public abstract record HashCodeBase
            {
                public override int GetHashCode() => 1;
            }

            public sealed partial record InheritsHashCode : HashCodeBase, IValue<int>;

            public abstract record EquatableBase : System.IEquatable<EquatableBase>
            {
                bool System.IEquatable<EquatableBase>.Equals(EquatableBase? other) => true;
            }

            public sealed partial record InheritsExplicitEquatable : EquatableBase, IValue<int>;

            public abstract record GenericEqualityBase<T>
            {
                public virtual bool Equals(GenericEqualityBase<T>? other) => ReferenceEquals(this, other);

                public override int GetHashCode() => 1;
            }

            public sealed partial record InheritsGenericEquality : GenericEqualityBase<int>, IValue<int>;

            public sealed partial record InheritsLibraryReferenceEquality : CodoMetis.TypeKit.Generators.Probes.ProbeReferenceEqualityRecord, IValue<int>;

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

            // A record class's, whose parameter is nullable.
            public sealed partial record EqualsOnClass : IValue<string>
            {
                public bool Equals(EqualsOnClass? other) => true;
            }

            // An Equals of a deeper base record's type, which IEquatable<TBase> and so a collection of the base
            // reached, while == compared the wrapped value (#196). The direct base's is C#'s own CS0111.
            public abstract record DeepEqualityBase;

            public abstract record MiddleEqualityBase : DeepEqualityBase;

            public sealed partial record EqualsDeeperBase : MiddleEqualityBase, IValue<string>
            {
                public bool Equals(DeepEqualityBase? other) => ReferenceEquals(this, other);
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

            // An interface's explicit implementation of IClosedSet<TSelf>, which the generated Name and
            // TryParse replaced without a word (#191), and IParsable<TSelf> declared through an interface,
            // which failed the aspect (LAMA0512).
            public interface ISetNamesItself<TSelf> : IClosedSet<TSelf> where TSelf : class, ISetNamesItself<TSelf>
            {
                string IClosedSet<TSelf>.Name => "***";

                static bool IClosedSet<TSelf>.TryParse(string? name, out TSelf member) => throw new System.NotSupportedException();
            }

            public sealed partial class SetWithNameThroughInterface : IClosedSet<SetWithNameThroughInterface>, ISetNamesItself<SetWithNameThroughInterface> { public static readonly SetWithNameThroughInterface A = new(); }

            public sealed partial class SetWithNameThroughLibraryInterface : IClosedSet<SetWithNameThroughLibraryInterface>, CodoMetis.TypeKit.Generators.Probes.IProbeSetNames<SetWithNameThroughLibraryInterface> { public static readonly SetWithNameThroughLibraryInterface A = new(); }

            // A base class of another project that implements IClosedSet<TSelf> explicitly: All listed none through
            // the interface, its Name was the base's, and EF Core stored that and read no member's name back (#191).
            public sealed partial class SetOverLibraryBase : Library.ExplicitSetBase<SetOverLibraryBase> { public static readonly SetOverLibraryBase A = new(); }

            // A base class that declares a member the generators introduce into a set (#205): each failed the aspect
            // (LAMA0500, LAMA0502, LAMA0512) naming nothing the user wrote, and an unsealed ToString() was replaced
            // without a word. A sealed ToString() is the seam, as C# keeps it in every derived class.
            public abstract class SealedToStringSetBase { public sealed override string ToString() => "***"; }
            public sealed partial class SetOverSealedToStringBase : SealedToStringSetBase, IClosedSet<SetOverSealedToStringBase> { public static readonly SetOverSealedToStringBase A = new(); }
            public abstract class OpenToStringSetBase { public override string ToString() => "***"; }
            public sealed partial class SetOverOpenToStringBase : OpenToStringSetBase, IClosedSet<SetOverOpenToStringBase> { public static readonly SetOverOpenToStringBase A = new(); }
            public abstract class ParsingSetBase<TSelf> { public static TSelf Parse(string name) => throw new System.NotSupportedException(); }
            public sealed partial class SetOverParsingBase : ParsingSetBase<SetOverParsingBase>, IClosedSet<SetOverParsingBase> { public static readonly SetOverParsingBase A = new(); }
            public abstract class IgnoreCaseParsingSetBase<TSelf> { public static TSelf Parse(string name, bool ignoreCase) => throw new System.NotSupportedException(); }
            public sealed partial class SetOverIgnoreCaseParsingBase : IgnoreCaseParsingSetBase<SetOverIgnoreCaseParsingBase>, IClosedSet<SetOverIgnoreCaseParsingBase> { public static readonly SetOverIgnoreCaseParsingBase A = new(); }
            public abstract class TryParsingSetBase<TSelf> { public static bool TryParse(string? name, bool ignoreCase, out TSelf member) => throw new System.NotSupportedException(); }
            public sealed partial class SetOverTryParsingBase : TryParsingSetBase<SetOverTryParsingBase>, IClosedSet<SetOverTryParsingBase> { public static readonly SetOverTryParsingBase A = new(); }
            public sealed partial class SetWithTryParseByRef : IClosedSet<SetWithTryParseByRef> { public static readonly SetWithTryParseByRef A = new(); public static bool TryParse(string? name, ref SetWithTryParseByRef member) => throw new System.NotSupportedException(); }
            public abstract class TryParsingByRefSetBase<TSelf> { public static bool TryParse(string? name, bool ignoreCase, ref TSelf member) => throw new System.NotSupportedException(); }
            public sealed partial class SetOverTryParsingByRefBase : TryParsingByRefSetBase<SetOverTryParsingByRefBase>, IClosedSet<SetOverTryParsingByRefBase> { public static readonly SetOverTryParsingByRefBase A = new(); }
            public abstract class MatchingSetBase { public TResult Match<TResult>(System.Func<TResult> only) => only(); }
            public sealed partial class SetOverMatchingBase : MatchingSetBase, IClosedSet<SetOverMatchingBase> { public static readonly SetOverMatchingBase A = new(); }
            public abstract class ParsableSetBase<TSelf> : System.IParsable<TSelf> where TSelf : ParsableSetBase<TSelf>
            {
                static TSelf System.IParsable<TSelf>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();
                static bool System.IParsable<TSelf>.TryParse(string? s, System.IFormatProvider? provider, out TSelf result) { result = null!; return false; }
            }
            public sealed partial class SetOverParsableBase : ParsableSetBase<SetOverParsableBase>, IClosedSet<SetOverParsableBase> { public static readonly SetOverParsableBase A = new(); }
            public abstract class PrivateFieldsSetBase { private string _name = ""; private object? _all; public override int GetHashCode() => _name.Length + (_all is null ? 0 : 1); }
            public sealed partial class SetOverPrivateFieldsBase : PrivateFieldsSetBase, IClosedSet<SetOverPrivateFieldsBase> { public static readonly SetOverPrivateFieldsBase A = new(); }
            public abstract class QualifiedSetBase { public string QualifiedName => "***"; }
            public abstract class AllListingSetBase { public static int All => 0; }
            public abstract class ProtectedFieldSetBase { protected string _name = ""; }
            public sealed partial class SetOverProtectedFieldBase : ProtectedFieldSetBase, IClosedSet<SetOverProtectedFieldBase> { public static readonly SetOverProtectedFieldBase A = new(); }
            public sealed partial class SetOverAllListingBase : AllListingSetBase, IClosedSet<SetOverAllListingBase> { public static readonly SetOverAllListingBase A = new(); }
            public sealed partial class SetOverQualifiedBase : QualifiedSetBase, IClosedSet<SetOverQualifiedBase> { public static readonly SetOverQualifiedBase A = new(); }
            [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
            public abstract class AttributedSetBase { }
            public sealed partial class SetOverAttributedBase : AttributedSetBase, IClosedSet<SetOverAttributedBase> { public static readonly SetOverAttributedBase A = new(); }
            public sealed partial class SetOverLibrarySealedToString : Library.LibrarySealedToStringSetBase, IClosedSet<SetOverLibrarySealedToString> { public static readonly SetOverLibrarySealedToString A = new(); }
            public sealed partial class SetOverLibraryOpenToString : Library.LibraryOpenToStringSetBase, IClosedSet<SetOverLibraryOpenToString> { public static readonly SetOverLibraryOpenToString A = new(); }
            public sealed partial class SetOverLibraryParsingBase : Library.LibraryParsingSetBase<SetOverLibraryParsingBase>, IClosedSet<SetOverLibraryParsingBase> { public static readonly SetOverLibraryParsingBase A = new(); }
            public sealed partial class SetOverLibraryParsableBase : Library.LibraryParsableSetBase<SetOverLibraryParsableBase>, IClosedSet<SetOverLibraryParsableBase> { public static readonly SetOverLibraryParsableBase A = new(); }

            public interface ISetParsesItself<TSelf> : System.IParsable<TSelf> where TSelf : ISetParsesItself<TSelf>;

            public sealed partial class SetWithParsableThroughInterface : IClosedSet<SetWithParsableThroughInterface>, ISetParsesItself<SetWithParsableThroughInterface> { public static readonly SetWithParsableThroughInterface A = new(); }

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

            // The second member's first letter is the Kelvin sign (U+212A), written as an escape: ignoring
            // case ordinally tells it from K, and Match's parameters, lower-cased, do not (LAMA0611, CS0100).
            public sealed partial class SetWithKelvinSign : IClosedSet<SetWithKelvinSign> { public static readonly SetWithKelvinSign Kelvin = new(); public static readonly SetWithKelvinSign \u212Aelvin = new(); }

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

        private readonly string _libraryDirectory = Path.Combine(RepositoryRoot(), "artifacts", $"library-{Guid.NewGuid():N}");

        /// <summary>
        /// A library the generators do not run in, which references the base package alone, as a consumer's
        /// shared project may. A base type there that carries a marker or <c>IClosedSet&lt;TSelf&gt;</c> is no
        /// value object or set of its own, where in a project with the generators it would be refused
        /// (CMTK1022, CMTK1014), and Metalama lists no private member of its types (#191, #195).
        /// </summary>
        private const string Library =
            """
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ClosedSets;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Library;

            public enum LibraryFault { Refused }

            // Generic code calling T.Create reached this, which refused what the value object's own accepted.
            public abstract record StrictCreateBase<TSelf> : IValidatedValue<TSelf, string, LibraryFault> where TSelf : StrictCreateBase<TSelf>, IValidatedValue<TSelf, string, LibraryFault>
            {
                static Result<TSelf, LibraryFault> IValidatedValue<TSelf, string, LibraryFault>.Create(string value) => Result.Error(LibraryFault.Refused);
            }

            public abstract record PublicCreateBase<TSelf> : IValidatedValue<TSelf, string, LibraryFault> where TSelf : PublicCreateBase<TSelf>, IValidatedValue<TSelf, string, LibraryFault>
            {
                public static Result<TSelf, LibraryFault> Create(string value) => Result.Error(LibraryFault.Refused);
            }

            // A value-object contract declared through an interface of another project (#192).
            public interface ILibraryEntityIdentifier<TSelf> : IValueObject<TSelf, System.Guid> where TSelf : ILibraryEntityIdentifier<TSelf>
            {
                System.Guid IValueObject<TSelf, System.Guid>.Value => System.Guid.Empty;

                static bool IValueObject<TSelf, System.Guid>.operator ==(TSelf left, TSelf right) => false;

                static bool IValueObject<TSelf, System.Guid>.operator !=(TSelf left, TSelf right) => true;
            }

            // A public static implementation of a static abstract member reads as an explicit one from here.
            public abstract record PublicMinMaxBase<TSelf> : System.Numerics.IMinMaxValue<TSelf> where TSelf : PublicMinMaxBase<TSelf>, System.Numerics.IMinMaxValue<TSelf>
            {
                public static TSelf MinValue => throw new System.NotSupportedException();

                public static TSelf MaxValue => throw new System.NotSupportedException();
            }

            // Base classes of a closed set that declare a member the generators introduce into it (#205).
            public abstract class LibrarySealedToStringSetBase { public sealed override string ToString() => "***"; }

            public abstract class LibraryOpenToStringSetBase { public override string ToString() => "***"; }

            public abstract class LibraryParsingSetBase<TSelf> { public static TSelf Parse(string name) => throw new System.NotSupportedException(); }

            public abstract class LibraryParsableSetBase<TSelf> : System.IParsable<TSelf> where TSelf : LibraryParsableSetBase<TSelf>
            {
                static TSelf System.IParsable<TSelf>.Parse(string s, System.IFormatProvider? provider) => throw new System.NotSupportedException();

                static bool System.IParsable<TSelf>.TryParse(string? s, System.IFormatProvider? provider, out TSelf result) { result = null!; return false; }
            }

            // The set's All listed none, its Name through the interface was the base's, and EF Core stored that.
            public abstract class ExplicitSetBase<TSelf> : IClosedSet<TSelf> where TSelf : ExplicitSetBase<TSelf>
            {
                string IClosedSet<TSelf>.Name => "***";

                string IClosedSet<TSelf>.QualifiedName => "***";

                static System.Collections.Generic.IReadOnlyList<TSelf> IClosedSet<TSelf>.All => [];

                static bool IClosedSet<TSelf>.TryParse(string? name, out TSelf member) => throw new System.NotSupportedException();
            }
            """;

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
            var library = await BuildLibrary(_libraryDirectory, Library);
            var declarations = Build(_directory, new Dictionary<string, string> { ["Declarations.cs"] = Declarations, ["OtherFile.cs"] = OtherFile }, library);
            var sets = Build(_setsDirectory, new Dictionary<string, string> { ["RefusedSets.cs"] = RefusedSets }, library);

            var (declarationOutput, declarationErrors) = await declarations;
            var (setOutput, setErrors) = await sets;

            DeclarationErrors = declarationErrors;
            Errors = [.. declarationErrors.Concat(setErrors).Distinct()];
            Output = declarationOutput + setOutput;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var directory in new[] { _directory, _setsDirectory, _libraryDirectory })
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Builds a consumer of the generators and the analyzer in <paramref name="directory"/> from
        /// <paramref name="sources"/>, and reads its errors. A declaration whose build error is the
        /// compiler's own (CS0111) belongs in a consumer of its own: beside it, Metalama reported no error
        /// in the code it generated into a declaration's own file, which the other guards rely on.
        /// <paramref name="library"/>, a project <see cref="BuildLibrary"/> built, is referenced too.
        /// </summary>
        internal static async Task<(string Output, IReadOnlyList<Diagnostic> Errors)> Build(string directory, IReadOnlyDictionary<string, string> sources, string? library = null)
        {
            Directory.CreateDirectory(directory);

            var src = Path.Combine(RepositoryRoot(), "src");
            var probes = Path.Combine(RepositoryRoot(), "test", "CodoMetis.TypeKit.Generators.Probes", "CodoMetis.TypeKit.Generators.Probes.csproj");
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
                     <ProjectReference Include="{probes}" />
                     {(library is null ? "" : $"<ProjectReference Include=\"{library}\" />")}
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
                dotnet_diagnostic.CMTK1023.severity = error
                """);

            await WriteBuildStoppers(directory);

            var (_, text) = await RunBuild(directory);
            IReadOnlyList<Diagnostic> found = [.. DiagnosticLine().Matches(text).Select(match => new Diagnostic(match.Groups["id"].Value, match.Groups["message"].Value)).Distinct()];

            found.ShouldNotBeEmpty($"The consumer build reported no errors at all, so it did not run as intended:{Environment.NewLine}{text}");
            return (text, found);
        }

        /// <summary>
        /// Builds a library of <paramref name="source"/> in <paramref name="directory"/> that references the base
        /// package alone, so the generators do not run in it, and returns its project file. It must build.
        /// </summary>
        internal static async Task<string> BuildLibrary(string directory, string source)
        {
            Directory.CreateDirectory(directory);

            var project = Path.Combine(directory, "Library.csproj");
            await File.WriteAllTextAsync(project,
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <Nullable>enable</Nullable>
                     <ImplicitUsings>enable</ImplicitUsings>
                   </PropertyGroup>
                   <ItemGroup>
                     <ProjectReference Include="{Path.Combine(RepositoryRoot(), "src")}/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj" />
                   </ItemGroup>
                 </Project>
                 """);
            await File.WriteAllTextAsync(Path.Combine(directory, "Library.cs"), source);
            await WriteBuildStoppers(directory);

            var (exitCode, text) = await RunBuild(directory);
            exitCode.ShouldBe(0, $"The library the consumers reference did not build:{Environment.NewLine}{text}");
            return project;
        }

        /// <summary>
        /// Stops MSBuild's upward search in <paramref name="directory"/>, so the repository's own build settings
        /// (warnings as errors, documentation, central package management) do not apply there.
        /// </summary>
        private static async Task WriteBuildStoppers(string directory)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        }

        /// <summary>
        /// Builds the project in <paramref name="directory"/>. --no-dependencies and RestoreRecursive=false: the
        /// referenced projects are already built by this test run (the library before its consumers), and
        /// restoring them again could rewrite restore output that other tests read concurrently. Already built in
        /// this run's configuration, which is therefore passed on: without it the consumer builds in Debug and, in
        /// a Release run such as CI's, finds no referenced assembly at all.
        /// </summary>
        private static async Task<(int ExitCode, string Output)> RunBuild(string directory)
        {
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

            return (process.ExitCode, await output + await errors);
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
