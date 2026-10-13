using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Diagnostics;

namespace CodoMetis.TypeKit.Generators;

/// <summary>
/// The errors the generators report on a value object they cannot generate. The ids are public
/// contract, like the analyzer's.
/// </summary>
[CompileTime]
internal static class AspectDiagnostics
{
    /// <summary>
    /// Ends every message: the Build errors table of the generators' README explains each id. A
    /// Metalama 2026.1 <c>DiagnosticDefinition</c> takes no help link, unlike an analyzer's
    /// descriptor, so the message carries it (BuildOutcomeTests).
    /// </summary>
    private const string SeeBuildErrors = " See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors";

    public static readonly DiagnosticDefinition<INamedType> MissingPartialKeyword =
        new("CMTK1000", Severity.Error, "'{0}' is a value object and must be declared partial." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> MissingRecordKeyword =
        new("CMTK1001", Severity.Error, "'{0}' is a value object and must be declared as a record." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> MissingReadonlyKeyword =
        new("CMTK1002", Severity.Error, "'{0}' is a value object and must be declared readonly." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Markers)> MoreThanOneMarker =
        new("CMTK1003", Severity.Error, "'{0}' implements more than one value-object marker ({1}), so it is not generated." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, INamedType Marker)> MarkerNamesAnotherType =
        new("CMTK1004", Severity.Error, "'{0}' implements {1}, whose first type argument must be '{0}' itself, so it is not generated." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Reason)> UnsupportedValueObject =
        new("CMTK1005", Severity.Error, "'{0}' cannot be generated as a value object: {1}." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> MissingSealedKeyword =
        new("CMTK1006", Severity.Error, "'{0}' is a value object and must be declared sealed, so it is not generated." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string ClassName, string Owner)> CompanionClassNameTaken =
        new("CMTK1007", Severity.Error, "'{0}' gets GetValue() and ValueOrNull() in a class named '{1}', but {2} already has that name, so the class is not generated. Rename one of them." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Members)> ComparisonMemberBesideTheSeam =
        new("CMTK1008", Severity.Error, "'{0}' declares {1}, but its comparison is generated from CompareTo({0}): declare that one to change the order, and remove the rest." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Constructors)> HandWrittenConstructor =
        new("CMTK1009", Severity.Error, "'{0}' declares {1}, so it is not generated: its only constructor and its Value are generated, and every way in goes through From or Create. Remove it; where another way in is needed, declare a static method that calls From or Create." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, int Parts)> DeclaredInSeveralParts =
        new("CMTK1010", Severity.Error, "'{0}' is declared in {1} parts, so it is not generated: declare a value object in a single part, and move the members of the other parts into it." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Declared, string Remedy)> HandWrittenGeneratedMember =
        new("CMTK1011", Severity.Error, "'{0}' declares {1}, so it is not generated: {2}." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Members)> StateBesideTheWrappedValue =
        new("CMTK1012", Severity.Error, "'{0}' holds {1} besides its wrapped value, so it is not generated: its JSON, parsing, type converter and EF Core column carry the wrapped value alone, so the rest is lost on every round trip, and the record's equality compares it too. Compute it from Value in a property without a backing field, make it static, or wrap a type of your own that holds all of it." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetNotPartial =
        new("CMTK1013", Severity.Error, "'{0}' is a closed set and must be declared partial." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetNotSealed =
        new("CMTK1014", Severity.Error, "'{0}' is a closed set and must be declared sealed, so it is not generated: a set with subtypes is not closed." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetNoMembers =
        new("CMTK1015", Severity.Error, "'{0}' declares no member, so it is not generated: a member is a public static readonly field of the set's own type." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetPublicConstructor =
        new("CMTK1016", Severity.Error, "'{0}' has a constructor that is not private, so it is not generated: an instance beside its members could be made. Make the constructor private." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType, string, string)> ClosedSetMemberNotAField =
        new("CMTK1017", Severity.Error, "'{0}.{1}' is {2} of the set's own type, so '{0}' is not generated: a member is a public static readonly field, and nothing else of that type is public and static." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetGeneric =
        new("CMTK1018", Severity.Error, "'{0}' is generic or nested in a generic type, so it is not generated: a closed set's members are named instances, which a type parameter cannot have." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType, string)> ClosedSetMemberNotCreated =
        new("CMTK1019", Severity.Error, "'{0}.{1}' is not initialized with new, so '{0}' is not generated: each member is its own instance, created where it is declared. A member initialized with another would give that instance two names, and one left null would have none." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<INamedType> ClosedSetRecord =
        new("CMTK1020", Severity.Error, "'{0}' is a record, so it is not generated: 'with' copies a member into an instance that is none, and a record compares by value where a closed set's members are singletons. Declare it a sealed partial class." + SeeBuildErrors);

    public static readonly DiagnosticDefinition<(INamedType Type, string Member, string Reason)> ClosedSetMemberNameTaken =
        new("CMTK1021", Severity.Error, "'{0}.{1}' {2}, so '{0}' is not generated: rename the member." + SeeBuildErrors);
}
