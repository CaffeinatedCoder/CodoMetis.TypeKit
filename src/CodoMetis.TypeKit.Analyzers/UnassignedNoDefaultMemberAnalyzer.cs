using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// CMTK0006: a field or auto-property of a class whose type forbids <c>default</c>, which no
/// initializer, <c>required</c> or constructor sets, so it starts as a <c>default</c> instance.
/// </summary>
/// <remarks>
/// <para>
/// This is the CS8618 that nullable analysis does not give structs, and the most common way left to
/// hold a default value object: <c>public OrderId Id { get; set; }</c> on an entity or a request type
/// that someone forgets to set, or that JSON leaves out. <c>required</c> closes it for object
/// initializers and for System.Text.Json alike.
/// </para>
/// <para>
/// Measured 2026-09-28: on the repository's own EF entities and request types all 12 reports were
/// settable properties of a type with only the implicit constructor, each a real way to a default
/// value object. <c>required</c> on such properties works with EF Core's compiled model, precompiled
/// queries and Native AOT (the consumer smoke test, rerun with it), so the fix the message names is
/// safe on entities too.
/// </para>
/// <para>
/// A parameterless constructor that is not public is exempt: that is the constructor EF Core and the
/// serializers materialize through before they set the properties, which is how entities declare it.
/// A record's copy constructor copies every member and is exempt too. A constructor that chains to
/// another with <c>this(…)</c> assigns what that one assigns. An assignment on any path counts, so
/// the rule errs towards silence.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnassignedNoDefaultMemberAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnassignedNoDefaultMember,
        title: "Member starts as a default instance",
        messageFormat: "'{0}' starts as a default '{1}', which passed no factory: it has no initializer, is not 'required', and {2} does not assign it. Make it 'required', initialize it, or assign it in every constructor.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A field or auto-property of a value object, Option, Result or [RequireCustomInitialization] struct type holds a default instance until something assigns it. Nullable analysis reports this for reference types (CS8618) but not for structs."
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (TypeKitSymbols.Resolve(start.Compilation) is not { } symbols) return;

            start.RegisterSymbolStartAction(type => AnalyzeType(type, symbols), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolStartAnalysisContext context, TypeKitSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsStatic) return;

        var candidates = Candidates(type, symbols);
        if (candidates.Count == 0) return;

        var initialized  = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
        var constructors = new ConcurrentDictionary<IMethodSymbol, Assignments>(SymbolEqualityComparer.Default);

        context.RegisterOperationAction(operation =>
        {
            foreach (var field in ((IFieldInitializerOperation)operation.Operation).InitializedFields) initialized[field] = true;
        }, OperationKind.FieldInitializer);

        context.RegisterOperationAction(operation =>
        {
            foreach (var property in ((IPropertyInitializerOperation)operation.Operation).InitializedProperties) initialized[property] = true;
        }, OperationKind.PropertyInitializer);

        context.RegisterOperationBlockAction(block =>
        {
            if (block.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
             || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, type)) return;

            constructors[constructor] = Assignments.Of(block.OperationBlocks, type);
        });

        context.RegisterSymbolEndAction(end =>
        {
            foreach (var (member, memberType) in candidates)
            {
                if (initialized.ContainsKey(member)) continue;

                var missing = type.InstanceConstructors.FirstOrDefault(constructor =>
                    !IsExempt(constructor, type) && !Assigns(constructor, member, constructors, []));

                if (missing is null) continue;

                end.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    member.Locations[0],
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    Describe(missing, type)));
            }
        });
    }

    /// <summary>Instance fields and auto-properties whose type forbids <c>default</c>, and that are not <c>required</c>.</summary>
    private static List<(ISymbol Member, ITypeSymbol Type)> Candidates(INamedTypeSymbol type, TypeKitSymbols symbols)
    {
        var members = type.GetMembers();

        // An auto-property is a property with a backing field; a property with hand-written accessors
        // holds nothing of its own.
        var backed = new HashSet<ISymbol>(
            members.OfType<IFieldSymbol>().Where(field => field.AssociatedSymbol is IPropertySymbol).Select(field => field.AssociatedSymbol!),
            SymbolEqualityComparer.Default);

        var candidates = new List<(ISymbol, ITypeSymbol)>();

        foreach (var member in members)
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false, IsConst: false, IsImplicitlyDeclared: false, IsRequired: false } field
                    when symbols.IsNoDefaultStruct(field.Type):
                    candidates.Add((field, field.Type));
                    break;

                // A positional record's property is set by its primary constructor's parameter.
                case IPropertySymbol { IsStatic: false, IsIndexer: false, IsRequired: false } property
                    when backed.Contains(property)
                      && !property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ParameterSyntax)
                      && symbols.IsNoDefaultStruct(property.Type):
                    candidates.Add((property, property.Type));
                    break;
            }
        }

        return candidates;
    }

    /// <summary>
    /// A parameterless constructor that is not public is the materializing one (EF Core, serializers),
    /// and a record's copy constructor copies every member.
    /// </summary>
    private static bool IsExempt(IMethodSymbol constructor, INamedTypeSymbol type) =>
        constructor is { IsImplicitlyDeclared: false, Parameters.Length: 0 } && constructor.DeclaredAccessibility != Accessibility.Public
     || constructor is { IsImplicitlyDeclared: true, Parameters.Length: 1 } && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);

    private static bool Assigns(IMethodSymbol constructor, ISymbol member, ConcurrentDictionary<IMethodSymbol, Assignments> constructors, HashSet<IMethodSymbol> visited)
    {
        if (!visited.Add(constructor) || !constructors.TryGetValue(constructor, out var assignments)) return false;

        return assignments.Members.Contains(member)
            || assignments.ChainsTo is { } target && Assigns(target, member, constructors, visited);
    }

    private static string Describe(IMethodSymbol constructor, INamedTypeSymbol type) =>
        constructor.IsImplicitlyDeclared
            ? "the implicit constructor"
            : $"the constructor '{type.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})'";

    /// <summary>What one constructor body assigns on <c>this</c>, and the constructor it chains to with <c>this(…)</c>.</summary>
    private sealed class Assignments(HashSet<ISymbol> members, IMethodSymbol? chainsTo)
    {
        public HashSet<ISymbol> Members { get; } = members;

        public IMethodSymbol? ChainsTo { get; } = chainsTo;

        public static Assignments Of(ImmutableArray<IOperation> blocks, INamedTypeSymbol type)
        {
            var members = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            IMethodSymbol? chainsTo = null;

            foreach (var operation in blocks.SelectMany(block => block.DescendantsAndSelf()))
            {
                switch (operation)
                {
                    case IAssignmentOperation assignment:
                        Collect(assignment.Target, members);
                        break;

                    case IArgumentOperation { Parameter.RefKind: RefKind.Out or RefKind.Ref } argument:
                        Collect(argument.Value, members);
                        break;

                    case IInvocationOperation { TargetMethod.MethodKind: MethodKind.Constructor, Instance: IInstanceReferenceOperation } chained
                        when SymbolEqualityComparer.Default.Equals(chained.TargetMethod.ContainingType, type):
                        chainsTo = chained.TargetMethod;
                        break;
                }
            }

            return new Assignments(members, chainsTo);
        }

        private static void Collect(IOperation target, HashSet<ISymbol> members)
        {
            switch (target)
            {
                case IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field:
                    members.Add(field.Field.AssociatedSymbol ?? field.Field);
                    break;

                case IPropertyReferenceOperation { Instance: IInstanceReferenceOperation } property:
                    members.Add(property.Property);
                    break;

                case ITupleOperation tuple:
                    foreach (var element in tuple.Elements) Collect(element, members);
                    break;
            }
        }
    }
}
