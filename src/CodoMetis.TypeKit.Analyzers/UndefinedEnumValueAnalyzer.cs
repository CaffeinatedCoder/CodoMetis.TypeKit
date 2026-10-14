using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0014: a constant of one of the enums CodoMetis.TypeKit takes as input, <c>FaultKind</c> or
/// <c>ConstructorAccessibility</c>, that names none of its members: <c>(FaultKind)9</c>,
/// <c>default(FaultKind)</c>, <c>[RequireParameterlessConstructor((ConstructorAccessibility)9)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// C# lets any number be an enum. A fault set's member of no kind compiles and stops the host when it
/// maps the set; a constructor accessibility no member names compiled and CMTK0011 read it as
/// <c>Public</c>, without a word. Both are reported where the value is written. The members are read
/// from the enum's own fields, so a member a newer base package adds is defined here too.
/// </para>
/// <para>
/// A value only compared, <c>kind == default</c>, <c>kind is (FaultKind)0</c> or a <c>case</c>, is a
/// guard and is not reported. Where a value is folded from others, by a binary or a unary operator or
/// a constant conditional, only the outermost expression is judged, since its value is the one that
/// reaches its use: <c>(FaultKind)0 + 1</c> is <c>NotFound</c>, and <c>~FaultKind.NotFound</c> is
/// reported.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UndefinedEnumValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UndefinedEnumValue,
        title: "Value names no member of its enum",
        messageFormat: "{0} names no member of {1}: {2}",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "C# lets any number be an enum, and CodoMetis.TypeKit reads FaultKind and ConstructorAccessibility as one of their members: a fault set's member of no kind stops the host where the set is mapped, and an accessibility no member names was read as public.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.UndefinedEnumValue)
    );

    /// <summary>The enums, by metadata name, with what an undefined value does there.</summary>
    private static readonly (string MetadataName, string Consequence)[] Enums =
    [
        ("CodoMetis.TypeKit.ClosedSets.FaultKind", "a host refuses a fault set's member of no kind where it maps the set, when it starts"),
        ("CodoMetis.TypeKit.ConstructorAccessibility", "CMTK0011 reads it as public"),
    ];

    /// <summary>The operations that can be a constant of an enum, folded or not; each is judged only where no other contains it.</summary>
    private static readonly ImmutableArray<OperationKind> Kinds =
        [OperationKind.Conversion, OperationKind.DefaultValue, OperationKind.Binary, OperationKind.Unary, OperationKind.Conditional];

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCode.AnalysisFlags);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var enums = new Dictionary<INamedTypeSymbol, (HashSet<object> Members, string Consequence)>(SymbolEqualityComparer.Default);
            foreach (var (metadataName, consequence) in Enums)
            {
                foreach (var type in start.Compilation.GetTypesByMetadataName(metadataName))
                {
                    if (type.TypeKind != TypeKind.Enum) continue;

                    var members = new HashSet<object>(type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue).Select(field => field.ConstantValue!));
                    enums[type] = (members, consequence);
                }
            }

            if (enums.Count == 0) return;

            start.RegisterOperationAction(operation => Analyze(operation, enums), Kinds);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Dictionary<INamedTypeSymbol, (HashSet<object> Members, string Consequence)> enums)
    {
        var operation = context.Operation;
        if (Undefined(operation, enums) is not { } enumeration) return;

        // Only the outermost of a folded expression reaches its use, whatever an operand names; and a
        // value only compared is a guard.
        if (IsFoldedFurther(operation, enums)) return;
        if (IsComparison(operation)) return;

        context.Report(Diagnostic.Create(
            Rule,
            operation.Syntax.GetLocation(),
            operation.Syntax.ToString(),
            enumeration.Type.Name,
            enumeration.Consequence));
    }

    /// <summary>The enum <paramref name="operation"/> is a constant of, where its value names none of the members.</summary>
    private static (INamedTypeSymbol Type, string Consequence)? Undefined(IOperation operation, Dictionary<INamedTypeSymbol, (HashSet<object> Members, string Consequence)> enums)
    {
        if (operation.Type is not INamedTypeSymbol type || !enums.TryGetValue(type, out var known)) return null;
        if (!operation.ConstantValue.HasValue || operation.ConstantValue.Value is not { } value) return null;

        return known.Members.Contains(value) ? null : (type, known.Consequence);
    }

    /// <summary>Whether <paramref name="operation"/> is an operand of a larger constant of an enum, which is judged in its place.</summary>
    private static bool IsFoldedFurther(IOperation operation, Dictionary<INamedTypeSymbol, (HashSet<object> Members, string Consequence)> enums) =>
        operation.Parent is { } parent
        && Kinds.Contains(parent.Kind)
        && parent.Type is INamedTypeSymbol type
        && enums.ContainsKey(type)
        && parent.ConstantValue.HasValue;

    /// <summary>Whether the value is only compared: an equality or a relational operator, or a pattern.</summary>
    private static bool IsComparison(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual }:
                case IPatternOperation:
                case ICaseClauseOperation:
                    return true;
                case IConversionOperation { IsImplicit: true }:
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }
}
