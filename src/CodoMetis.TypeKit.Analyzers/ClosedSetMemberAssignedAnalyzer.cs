using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0013: a closed set's member written anywhere but its own declaration.
/// </summary>
/// <remarks>
/// <para>
/// The generators give each member its name and fill <c>All</c> after the static field initializers
/// and before the static constructor's body. A member assigned in that body, <c>Open = Closed;</c>,
/// keeps the field pointing at another member: <c>Closed.Match</c> takes the <c>open</c> branch,
/// <c>Parse("Open")</c> answers an instance written as <c>"Closed"</c>, and <c>All</c> still lists the
/// instance the field no longer holds. One assigned in another static field's initializer, before the
/// names, gives one instance two. All of it compiles, and C# allows such a write nowhere else: a
/// <c>static readonly</c> field is written only by its type's static initializers and constructor.
/// </para>
/// <para>
/// A write is an assignment, compound or <c>??=</c> included, a deconstruction into the field, an
/// increment, a <c>ref</c> or <c>out</c> argument, and a writable <c>ref</c> taken to it, through which it
/// can be written later (a <c>ref readonly</c> one, declared or reassigned, only reads); each also where the field is a branch of a <c>ref</c> conditional,
/// <c>(flag ? ref Open : ref Closed) = Closed</c>. The field's own initializer is no reference to it, and CMTK1019 holds it to <c>new</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedSetMemberAssignedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ClosedSetMemberAssigned,
        title: "A closed set's member written outside its declaration",
        messageFormat: "'{0}.{1}' is written outside its declaration, after the generators named the instance it held and listed it in All: Match, Parse and the JSON would answer for another member, or none. Create each member with new where it is declared, and never write it again.",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A closed set's members are named and listed once, after the static field initializers and before the static constructor's body. A member written in either then holds an instance whose name and place in All are another's.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.ClosedSetMemberAssigned)
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCode.AnalysisFlags);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            start.RegisterOperationAction(operation => Analyze(operation, symbols), OperationKind.FieldReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var reference = (IFieldReferenceOperation)context.Operation;
        var field     = reference.Field;

        if (field is not { IsStatic: true, IsReadOnly: true } || !SymbolEqualityComparer.Default.Equals(field.Type, field.ContainingType)) return;
        if (!IsWrite(reference)) return;
        if (field.ContainingType.TypeKind != TypeKind.Class || symbols.FindClosedSetMarker(field.ContainingType) is null) return;

        context.Report(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), field.ContainingType.Name, field.Name));
    }

    /// <summary>Whether the reference writes the field, or hands out a <c>ref</c> through which it can be written.</summary>
    private static bool IsWrite(IOperation reference)
    {
        // (Open, Closed) = (Closed, Open), and flag ? ref Open : ref Closed: the field is an element of a
        // tuple, or a branch of a ref conditional, possibly nested, and that is what is written.
        var target = reference;
        while (target.Parent is ITupleOperation || target.Parent is IConditionalOperation { IsRef: true } conditional && conditional.Condition != target)
            target = target.Parent;

        return target.Parent switch
        {
            IDeconstructionAssignmentOperation deconstruction when deconstruction.Target == target => true,
            IAssignmentOperation assignment when assignment.Target == target                    => true,
            ISimpleAssignmentOperation { IsRef: true } assignment when assignment.Value == target => IsWritableRef(assignment.Target),
            IIncrementOrDecrementOperation                                                      => true,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }                => true,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.Ref } } => true,
            _                                                                                   => false,
        };
    }

    /// <summary>
    /// Whether a ref reassigned to the field can write it: a <c>ref readonly</c> local, parameter or
    /// field only reads, <c>alias = ref Open</c> as much as its declaration.
    /// </summary>
    private static bool IsWritableRef(IOperation target) => target switch
    {
        ILocalReferenceOperation local         => local.Local.RefKind == RefKind.Ref,
        IParameterReferenceOperation parameter => parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out,
        IFieldReferenceOperation field         => field.Field.RefKind == RefKind.Ref,
        _                                      => true,
    };
}
