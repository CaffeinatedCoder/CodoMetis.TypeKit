using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
/// <para>
/// A <c>required</c> member is set by every object initializer, except behind a constructor marked
/// <c>[SetsRequiredMembers]</c>, which promises to set it instead; such a constructor that does not
/// is reported. The message names <c>required</c> only where it compiles: not on a get-only property
/// or a <c>readonly</c> field (CS9034), nor on a member, or a setter, less visible than the class (CS9032).
/// </para>
/// <para>
/// In a project that declares value objects, Metalama runs analyzers on the source before weaving,
/// where <c>this(OrderId.New())</c> does not bind. The constructor it chains to is then the one
/// candidate that takes that many arguments; if there is none or more than one, the chaining
/// constructor counts as assigning everything.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnassignedNoDefaultMemberAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnassignedNoDefaultMember,
        title: "Member starts as a default instance",
        messageFormat: "'{0}' starts as a default '{1}', which passed no factory: {2}. {3}.",
        category: "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A field or auto-property of a value object, Option, Result or [RequireCustomInitialization] struct type holds a default instance until something assigns it. Nullable analysis reports this for reference types (CS8618) but not for structs.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.UnassignedNoDefaultMember)
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

            var setsRequiredMembers = start.Compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

            start.RegisterSymbolStartAction(type => AnalyzeType(type, symbols, setsRequiredMembers), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolStartAnalysisContext context, TypeKitSymbols symbols, INamedTypeSymbol? setsRequiredMembers)
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

                // Every object initializer sets a required member, except behind [SetsRequiredMembers].
                var required = member is IFieldSymbol { IsRequired: true } or IPropertySymbol { IsRequired: true };

                var missing = type.InstanceConstructors.FirstOrDefault(constructor =>
                    !IsExempt(constructor, type)
                 && (!required || HasAttribute(constructor, setsRequiredMembers))
                 && !Assigns(constructor, member, constructors, []));

                if (missing is null) continue;

                var (reason, advice) = required
                    ? ($"it is 'required', but {Describe(missing, type)} carries [SetsRequiredMembers] and does not assign it", "Assign it in that constructor, or remove [SetsRequiredMembers] from it")
                    : ($"it has no initializer, is not 'required', and {Describe(missing, type)} does not assign it",
                       CanBeRequired(member, type) ? "Make it 'required', initialize it, or assign it in every constructor" : "Initialize it, or assign it in every constructor");

                end.Report(Diagnostic.Create(
                    Rule,
                    member.Locations[0],
                    member.Name,
                    memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    reason,
                    advice));
            }
        });
    }

    /// <summary>Instance fields and auto-properties whose type forbids <c>default</c>.</summary>
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
                case IFieldSymbol { IsStatic: false, IsConst: false, IsImplicitlyDeclared: false } field
                    when symbols.IsNoDefaultStruct(field.Type):
                    candidates.Add((field, field.Type));
                    break;

                // A positional record's property is set by its primary constructor's parameter.
                case IPropertySymbol { IsStatic: false, IsIndexer: false } property
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

        return assignments.AssignsEverything
            || assignments.Members.Contains(member)
            || assignments.ChainsTo is { } target && Assigns(target, member, constructors, visited);
    }

    private static bool HasAttribute(IMethodSymbol constructor, INamedTypeSymbol? attribute) =>
        attribute is not null && constructor.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));

    /// <summary>
    /// Whether <c>required</c> compiles on <paramref name="member"/>: a field that is not
    /// <c>readonly</c>, or a property with a setter or <c>init</c> that is not an explicit interface
    /// implementation, as visible as the class, setter included (CS9032, CS9034).
    /// </summary>
    private static bool CanBeRequired(ISymbol member, INamedTypeSymbol type)
    {
        var visibility = EffectiveAccessibility(type);

        return member switch
        {
            IFieldSymbol field       => !field.IsReadOnly && AtLeastAsVisible(field.DeclaredAccessibility, visibility),
            IPropertySymbol property => property is { SetMethod: { } setter, ExplicitInterfaceImplementations.IsEmpty: true }
                                     && AtLeastAsVisible(property.DeclaredAccessibility, visibility)
                                     && AtLeastAsVisible(setter.DeclaredAccessibility, visibility),
            _ => false
        };
    }

    /// <summary>The narrowest accessibility on the way out from <paramref name="type"/> through its containing types.</summary>
    private static Accessibility EffectiveAccessibility(INamedTypeSymbol type)
    {
        var effective = type.DeclaredAccessibility;

        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            effective = (effective, outer.DeclaredAccessibility) switch
            {
                (Accessibility.Internal, Accessibility.Protected) or (Accessibility.Protected, Accessibility.Internal) => Accessibility.ProtectedAndInternal,
                var (inner, around) => Rank(around) < Rank(inner) ? around : inner
            };
        }

        return effective;
    }

    private static int Rank(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public                                 => 5,
            Accessibility.ProtectedOrInternal                    => 4,
            Accessibility.Protected or Accessibility.Internal    => 3,
            Accessibility.ProtectedAndInternal                   => 2,
            _                                                    => 1
        };

    /// <summary>
    /// Whether a member of a class of <paramref name="container"/> visibility may be <c>required</c>
    /// (measured against CS9032): a protected member of a nested class is protected in that class, not
    /// in the one around it, so only a public member is as visible as a public or protected class.
    /// </summary>
    private static bool AtLeastAsVisible(Accessibility member, Accessibility container) =>
        container is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal
            ? member is Accessibility.Public
            : member is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static string Describe(IMethodSymbol constructor, INamedTypeSymbol type) =>
        constructor.IsImplicitlyDeclared
            ? "the implicit constructor"
            : $"the constructor '{type.Name}({string.Join(", ", constructor.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})'";

    /// <summary>
    /// What one constructor body assigns on <c>this</c>, and the constructor it chains to with
    /// <c>this(…)</c>; or that it assigns everything, when it chains to a constructor that cannot be told.
    /// </summary>
    private sealed class Assignments(HashSet<ISymbol> members, IMethodSymbol? chainsTo, bool assignsEverything)
    {
        public HashSet<ISymbol> Members { get; } = members;

        public IMethodSymbol? ChainsTo { get; } = chainsTo;

        public bool AssignsEverything { get; } = assignsEverything;

        public static Assignments Of(ImmutableArray<IOperation> blocks, INamedTypeSymbol type)
        {
            var members = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            IMethodSymbol? chainsTo = null;
            var assignsEverything = false;

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

                    // this(OrderId.New()) before weaving: the generated New does not bind, and neither
                    // does the chained call.
                    case IInvalidOperation { Syntax: ConstructorInitializerSyntax initializer } invalid when initializer.IsKind(SyntaxKind.ThisConstructorInitializer):
                        chainsTo          = UnboundTarget(initializer, invalid.SemanticModel, type);
                        assignsEverything = chainsTo is null;
                        break;
                }
            }

            return new Assignments(members, chainsTo, assignsEverything);
        }

        /// <summary>The one constructor of <paramref name="type"/> that an unbound <c>this(…)</c> can mean, judged by its arguments' count.</summary>
        private static IMethodSymbol? UnboundTarget(ConstructorInitializerSyntax initializer, SemanticModel? model, INamedTypeSymbol type)
        {
            if (model is null) return null;

            var count = initializer.ArgumentList.Arguments.Count;
            var info  = model.GetSymbolInfo(initializer);

            var candidates = (info.Symbol is { } bound ? [bound] : info.CandidateSymbols)
                             .OfType<IMethodSymbol>()
                             .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.ContainingType, type) && Accepts(candidate, count))
                             .Take(2)
                             .ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static bool Accepts(IMethodSymbol constructor, int count) =>
            constructor.Parameters.Count(parameter => !parameter.IsOptional && !parameter.IsParams) <= count
         && (count <= constructor.Parameters.Length || constructor.Parameters.LastOrDefault()?.IsParams == true);

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
