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
/// CMTK0010: a type argument that breaks a constraint its parameter declares by attribute,
/// <c>[Abstract]</c>, <c>[Sealed]</c>, <c>[Record]</c> or <c>[Concrete]</c>, at any closed use.
/// </summary>
/// <remarks>
/// <para>
/// The constraint is declared where C# cannot express it (<c>where T : abstract</c>), and the
/// violation is at the use site, which may be in any project, so this is an analyzer and not an
/// aspect. Every generic name written in source is checked where it is written, at the argument;
/// a call whose type arguments are inferred has no such name and is checked at the call, and a
/// method group whose type arguments are inferred from the delegate it converts to
/// (<c>Action&lt;PlaceOrder&gt; run = Runner.Run;</c>) at the group.
/// </para>
/// <para>
/// A type parameter given as the argument satisfies a constraint only when it carries the same
/// attribute, or a C# constraint that proves it (<c>struct</c> for <c>[Sealed]</c> and
/// <c>[Concrete]</c>, <c>new()</c> for <c>[Concrete]</c>). Otherwise the requirement would end at
/// the first forwarding parameter, and <c>Sub&lt;ConcreteCommand&gt;</c> would compile.
/// </para>
/// <para>
/// A method that implements an interface's method, or overrides a base class's, carries the
/// constraint of the method it implements whether or not it repeats the attribute, so a call through
/// the class is held to what the interface declared. A <c>cref</c> in a documentation comment names
/// the generic's own parameters and is not a use; an array is sealed and concrete.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TypeArgumentConstraintAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.TypeArgumentConstraint,
        title: "Type argument breaks a constraint its parameter declares",
        messageFormat: "'{0}', the type argument for '{1}' of '{2}', {3}",
        category: "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A type parameter marked [Abstract], [Sealed], [Record] or [Concrete] constrains its arguments where C# cannot. A closed use with an argument that breaks the constraint compiles; this rule is its error.",
        helpLinkUri: DiagnosticIds.HelpLink(DiagnosticIds.TypeArgumentConstraint)
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
            if (TypeKitSymbols.Resolve(start.Compilation) is not { HasTypeArgumentConstraints: true } symbols) return;

            start.RegisterSyntaxNodeAction(node => AnalyzeGenericName(node, symbols), SyntaxKind.GenericName);
            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, symbols), OperationKind.Invocation);
            start.RegisterOperationAction(operation => AnalyzeMethodGroup(operation, symbols), OperationKind.MethodReference);
        });
    }

    /// <summary>A generic name written in source, a type's or a method's: each argument against its parameter, reported at the argument.</summary>
    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var name = (GenericNameSyntax)context.Node;
        // <see cref="Box{T}"/> names the generic's own parameter, which carries nothing: not a use.
        if (name.IsUnboundGenericName || name.IsPartOfStructuredTrivia()) return;

        var info   = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        var (parameters, arguments) = symbol switch
        {
            INamedTypeSymbol type when type.IsGenericType => (type.OriginalDefinition.TypeParameters, type.TypeArguments),
            IMethodSymbol method when method.IsGenericMethod => (method.OriginalDefinition.TypeParameters, method.TypeArguments),
            _                                            => (ImmutableArray<ITypeParameterSymbol>.Empty, ImmutableArray<ITypeSymbol>.Empty),
        };

        if (parameters.Length != arguments.Length || parameters.Length != name.TypeArgumentList.Arguments.Count) return;

        for (var i = 0; i < parameters.Length; i++)
        {
            foreach (var breach in Breaches(symbols, parameters[i], arguments[i]))
                context.Report(Diagnostic.Create(Rule, name.TypeArgumentList.Arguments[i].GetLocation(), arguments[i].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), parameters[i].Name, Owner(symbol!), breach));
        }
    }

    /// <summary>A call whose type arguments are inferred: nothing is written, so it is reported at the call.</summary>
    private static void AnalyzeInvocation(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var expression = invocation.Syntax is InvocationExpressionSyntax call ? call.Expression : null;

        AnalyzeInferred(context, symbols, invocation.TargetMethod, expression, invocation.Syntax);
    }

    /// <summary>
    /// A method group whose type arguments are inferred from the delegate it converts to, as an
    /// assignment, an argument or a cast: nothing is written, so it is reported at the group.
    /// </summary>
    private static void AnalyzeMethodGroup(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var reference = (IMethodReferenceOperation)context.Operation;

        AnalyzeInferred(context, symbols, reference.Method, reference.Syntax, reference.Syntax);
    }

    private static void AnalyzeInferred(OperationAnalysisContext context, TypeKitSymbols symbols, IMethodSymbol method, SyntaxNode? name, SyntaxNode reported)
    {
        if (!method.IsGenericMethod) return;

        // Written type arguments are a generic name, checked where they are written.
        if (name is GenericNameSyntax || name is MemberAccessExpressionSyntax { Name: GenericNameSyntax }) return;

        var parameters = method.OriginalDefinition.TypeParameters;
        var arguments  = method.TypeArguments;
        if (parameters.Length != arguments.Length) return;

        for (var i = 0; i < parameters.Length; i++)
        {
            foreach (var breach in Breaches(symbols, parameters[i], arguments[i]))
                context.Report(Diagnostic.Create(Rule, reported.GetLocation(), arguments[i].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), parameters[i].Name, Owner(method), breach));
        }
    }

    /// <summary>The generic as its declaration names it: <c>Decider&lt;TCommand, TEvent&gt;</c>, <c>Runner.Run&lt;T&gt;</c>.</summary>
    private static string Owner(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type => type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IMethodSymbol method  => $"{method.ContainingType.Name}.{method.Name}<{string.Join(", ", method.OriginalDefinition.TypeParameters.Select(parameter => parameter.Name))}>",
        _                     => symbol.Name,
    };

    /// <summary>What <paramref name="argument"/> breaks of what <paramref name="parameter"/> declares, as the message's last clause.</summary>
    private static IEnumerable<string> Breaches(TypeKitSymbols symbols, ITypeParameterSymbol parameter, ITypeSymbol argument)
    {
        foreach (var constraint in Effective(symbols, parameter))
        {
            if (argument is ITypeParameterSymbol forwarded)
            {
                if (!Proves(symbols, forwarded, constraint))
                    yield return $"is a type parameter that does not carry [{constraint}]";
            }
            else if (!Satisfies(argument, constraint))
            {
                yield return constraint switch
                {
                    TypeArgumentConstraint.Abstract => "is not an abstract class or record",
                    TypeArgumentConstraint.Sealed   => "is not sealed",
                    TypeArgumentConstraint.Record   => "is not a record",
                    _                               => "is not a concrete class, record or struct",
                };
            }
        }
    }

    /// <summary>Whether <paramref name="argument"/> meets <paramref name="constraint"/>; an array is a sealed, concrete class.</summary>
    private static bool Satisfies(ITypeSymbol argument, TypeArgumentConstraint constraint) => constraint switch
    {
        TypeArgumentConstraint.Abstract => argument is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true, IsStatic: false },
        TypeArgumentConstraint.Sealed   => argument.TypeKind is TypeKind.Struct or TypeKind.Enum or TypeKind.Array || argument is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Delegate, IsSealed: true, IsStatic: false },
        TypeArgumentConstraint.Record   => argument is INamedTypeSymbol { IsRecord: true },
        _                               => argument.TypeKind is TypeKind.Array || argument is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false, IsStatic: false },
    };

    /// <summary>A forwarding type parameter proves a constraint by carrying the attribute, or by a C# constraint that implies it.</summary>
    private static bool Proves(TypeKitSymbols symbols, ITypeParameterSymbol forwarded, TypeArgumentConstraint constraint) =>
        Effective(symbols, forwarded).Contains(constraint)
     || (constraint is TypeArgumentConstraint.Sealed or TypeArgumentConstraint.Concrete && forwarded.HasValueTypeConstraint)
     || (constraint is TypeArgumentConstraint.Concrete && forwarded.HasConstructorConstraint);

    /// <summary>
    /// The constraints a type parameter declares, with those of the parameter it implements or
    /// overrides: <c>Sender.Send&lt;T&gt;</c> implementing <c>ISender.Send&lt;[Concrete] T&gt;</c> carries
    /// <c>[Concrete]</c> whether or not it repeats the attribute, so a call through the class is held
    /// to it. Without this, the requirement ended at the interface.
    /// </summary>
    private static IEnumerable<TypeArgumentConstraint> Effective(TypeKitSymbols symbols, ITypeParameterSymbol parameter)
    {
        var own = symbols.ConstraintsOn(parameter);
        if (parameter.DeclaringMethod is not { } method) return own;

        return own.Concat(Implemented(method.OriginalDefinition)
                              .Where(implemented => parameter.Ordinal < implemented.OriginalDefinition.TypeParameters.Length)
                              .SelectMany(implemented => Effective(symbols, implemented.OriginalDefinition.TypeParameters[parameter.Ordinal])))
                  .Distinct();
    }

    /// <summary>The methods <paramref name="method"/> overrides or implements, explicitly or implicitly, by its declaration.</summary>
    private static IEnumerable<IMethodSymbol> Implemented(IMethodSymbol method)
    {
        if (method.OverriddenMethod is { } overridden) yield return overridden;

        foreach (var explicitly in method.ExplicitInterfaceImplementations) yield return explicitly;

        if (method.ExplicitInterfaceImplementations.Length > 0 || method.IsStatic || method.DeclaredAccessibility != Accessibility.Public) yield break;

        foreach (var contract in method.ContainingType.AllInterfaces)
        {
            foreach (var member in contract.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (member.IsGenericMethod && SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member)?.OriginalDefinition, method))
                    yield return member;
            }
        }
    }
}
