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
    public static readonly DiagnosticDefinition<INamedType> MissingPartialKeyword =
        new("CMTK1000", Severity.Error, "'{0}' is a value object and must be declared partial");

    public static readonly DiagnosticDefinition<INamedType> MissingRecordKeyword =
        new("CMTK1001", Severity.Error, "'{0}' is a value object and must be declared as a record");

    public static readonly DiagnosticDefinition<INamedType> MissingReadonlyKeyword =
        new("CMTK1002", Severity.Error, "'{0}' is a value object and must be declared readonly");

    public static readonly DiagnosticDefinition<(INamedType Type, string Markers)> MoreThanOneMarker =
        new("CMTK1003", Severity.Error, "'{0}' implements more than one value-object marker ({1}), so it is not generated");

    public static readonly DiagnosticDefinition<(INamedType Type, INamedType Marker)> MarkerNamesAnotherType =
        new("CMTK1004", Severity.Error, "'{0}' implements {1}, whose first type argument must be '{0}' itself, so it is not generated");

    public static readonly DiagnosticDefinition<(INamedType Type, string Reason)> UnsupportedValueObject =
        new("CMTK1005", Severity.Error, "'{0}' cannot be generated as a value object: {1}");

    public static readonly DiagnosticDefinition<INamedType> MissingSealedKeyword =
        new("CMTK1006", Severity.Error, "'{0}' is a value object and must be declared sealed, so it is not generated");

    public static readonly DiagnosticDefinition<(INamedType Type, string ClassName, string Owner)> ExtensionClassNameTaken =
        new("CMTK1007", Severity.Error, "'{0}' gets GetValue() and ValueOrNull() in a class named '{1}', but {2} already has that name, so the class is not generated. Rename one of them.");

    public static readonly DiagnosticDefinition<(INamedType Type, string Members)> ComparisonMemberBesideTheSeam =
        new("CMTK1008", Severity.Error, "'{0}' declares {1}, but its comparison is generated from CompareTo({0}): declare that one to change the order, and remove the rest");
}
