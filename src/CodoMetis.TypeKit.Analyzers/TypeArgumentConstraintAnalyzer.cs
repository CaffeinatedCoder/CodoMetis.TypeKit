using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
/// (<c>Action&lt;PlaceOrder&gt; run = Runner.Run;</c>) at the group. A generic attribute's name binds
/// to its constructor and is checked as its type. A member of a generic C# 14 extension block takes
/// the block's type arguments from its receiver: a call, a property, a method group or an operator is
/// checked at the use, and a written list starts with the block's arguments.
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
/// the class is held to what the interface declared: a static method implementing a <c>static
/// abstract</c> one, and a base class's method implementing it for the derived class it is reached
/// through, included. A <c>cref</c> in a documentation comment names
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
            start.RegisterOperationAction(operation => AnalyzeProperty(operation, symbols), OperationKind.PropertyReference);
            start.RegisterOperationAction(
                operation => AnalyzeOperator(operation, symbols),
                OperationKind.Binary,
                OperationKind.Unary,
                OperationKind.Increment,
                OperationKind.Decrement,
                OperationKind.CompoundAssignment);
        });
    }

    /// <summary>
    /// A generic name written in source, a type's, an attribute's or a method's: each argument against
    /// its parameter, reported at the argument. A member of a generic extension block takes the
    /// block's arguments first and its own after them, <c>handler.Take&lt;Handler, Handler&gt;(handler)</c>.
    /// </summary>
    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context, TypeKitSymbols symbols)
    {
        var name = (GenericNameSyntax)context.Node;
        // <see cref="Box{T}"/> names the generic's own parameter, which carries nothing: not a use.
        if (name.IsUnboundGenericName || name.IsPartOfStructuredTrivia()) return;
        // nameof(Box<Handler>) is the text "Box": no argument of it is used.
        if (InNameOf(name, context.SemanticModel, context.CancellationToken)) return;

        var info   = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        var arguments = symbol switch
        {
            INamedTypeSymbol type when type.IsGenericType => Pairs(type),
            // [Mark<Handler>] binds to the attribute's constructor.
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { IsGenericType: true } type } => Pairs(type),
            IMethodSymbol method => Pairs(method),
            _                    => [],
        };

        if (arguments.Count == 0 || arguments.Count != name.TypeArgumentList.Arguments.Count) return;

        var receiver = symbol is IMethodSymbol { IsGenericMethod: true } ? Receiver(name, context.SemanticModel, context.CancellationToken) : null;

        for (var i = 0; i < arguments.Count; i++)
        {
            foreach (var diagnostic in Diagnostics(symbols, arguments[i], receiver, name.TypeArgumentList.Arguments[i].GetLocation()))
                context.Report(diagnostic);
        }
    }

    /// <summary>Whether <paramref name="name"/> is inside the argument of a <c>nameof</c>, the operator and not a method of that name.</summary>
    private static bool InNameOf(GenericNameSyntax name, SemanticModel model, CancellationToken token)
    {
        foreach (var invocation in name.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && invocation.ArgumentList.Span.Contains(name.Span)
             && model.GetOperation(invocation, token) is INameOfOperation)
                return true;
        }

        return false;
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

    /// <summary>A property of a generic extension block, <c>handler.Prop</c>: the block's arguments are inferred from the receiver.</summary>
    private static void AnalyzeProperty(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;

        AnalyzeInferred(context, symbols, reference.Property, null, reference.Syntax);
    }

    /// <summary>A user-defined operator of a generic extension block, <c>handler + 1</c>: the block's arguments are inferred from the operands.</summary>
    private static void AnalyzeOperator(OperationAnalysisContext context, TypeKitSymbols symbols)
    {
        var method = context.Operation switch
        {
            IBinaryOperation binary                 => binary.OperatorMethod,
            IUnaryOperation unary                   => unary.OperatorMethod,
            IIncrementOrDecrementOperation step     => step.OperatorMethod,
            ICompoundAssignmentOperation compound   => compound.OperatorMethod,
            _                                       => null,
        };

        if (method is not null) AnalyzeInferred(context, symbols, method, null, context.Operation.Syntax);
    }

    /// <summary>
    /// A member used with no generic name: the type arguments of its extension block, which are never
    /// written at such a use, and its own when they are inferred, all reported at <paramref name="reported"/>.
    /// </summary>
    private static void AnalyzeInferred(OperationAnalysisContext context, TypeKitSymbols symbols, ISymbol member, SyntaxNode? name, SyntaxNode reported)
    {
        // Written type arguments are a generic name, checked where they are written, the block's
        // included; holder?.Inst<T>() is a member binding.
        if (name is GenericNameSyntax or MemberAccessExpressionSyntax { Name: GenericNameSyntax } or MemberBindingExpressionSyntax { Name: GenericNameSyntax }) return;

        var arguments = Pairs(member);
        if (arguments.Count == 0) return;

        var receiver = member is IMethodSymbol { IsGenericMethod: true } ? Receiver(name ?? reported, context.Operation.SemanticModel, context.CancellationToken) : null;

        foreach (var argument in arguments)
        {
            foreach (var diagnostic in Diagnostics(symbols, argument, receiver, reported.GetLocation()))
                context.Report(diagnostic);
        }
    }

    /// <summary>
    /// One diagnostic per constraint <paramref name="argument"/> breaks, for the caller to report
    /// through <see cref="GeneratedCode"/>, which keeps generated code that maps to no markup quiet.
    /// </summary>
    private static IEnumerable<Diagnostic> Diagnostics(TypeKitSymbols symbols, TypeArgument argument, ITypeSymbol? receiver, Location location) =>
        Breaches(symbols, argument.Parameter, argument.Argument, receiver)
            .Select(breach => Diagnostic.Create(Rule, location, argument.Argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), argument.Parameter.Name, Owner(argument.Owner), breach));

    /// <summary>A type parameter, the argument it was given, and the generic that declares it.</summary>
    private readonly struct TypeArgument(ITypeParameterSymbol parameter, ITypeSymbol argument, ISymbol owner)
    {
        public ITypeParameterSymbol Parameter { get; } = parameter;

        public ITypeSymbol Argument { get; } = argument;

        public ISymbol Owner { get; } = owner;
    }

    /// <summary>The type arguments of a generic type, in order.</summary>
    private static List<TypeArgument> Pairs(INamedTypeSymbol type) =>
        Zip(type.OriginalDefinition.TypeParameters, type.TypeArguments, type);

    /// <summary>
    /// The type arguments a member's use gives, in the order they are written: its generic extension
    /// block's, then a method's own.
    /// </summary>
    private static List<TypeArgument> Pairs(ISymbol member)
    {
        var pairs = member.ContainingType is { IsExtension: true, IsGenericType: true } extension ? Pairs(extension) : [];

        if (member is IMethodSymbol { IsGenericMethod: true } method)
            pairs.AddRange(Zip(method.OriginalDefinition.TypeParameters, method.TypeArguments, method));

        return pairs;
    }

    private static List<TypeArgument> Zip(ImmutableArray<ITypeParameterSymbol> parameters, ImmutableArray<ITypeSymbol> arguments, ISymbol owner) =>
        parameters.Length == arguments.Length
            ? parameters.Zip(arguments, (parameter, argument) => new TypeArgument(parameter, argument, owner)).ToList()
            : [];

    /// <summary>
    /// The type a member is reached through, whose interfaces a method may implement by inheritance:
    /// <c>derived.Send&lt;T&gt;</c> binds to <c>BaseSender.Send&lt;T&gt;</c>, which implements
    /// <c>ISender.Send&lt;T&gt;</c> for <c>DerivedSender</c> alone. <paramref name="named"/> is the name,
    /// its member access or binding, or the call's expression; an unqualified name is reached through
    /// the type it is written in.
    /// </summary>
    private static ITypeSymbol? Receiver(SyntaxNode named, SemanticModel? model, CancellationToken token)
    {
        if (model is null) return null;

        if (named is SimpleNameSyntax simple)
        {
            named = simple.Parent switch
            {
                MemberAccessExpressionSyntax access when access.Name == simple => access,
                MemberBindingExpressionSyntax binding                           => binding,
                _                                                               => simple,
            };
        }

        var receiver = named switch
        {
            // base.Send<T>() runs on this object, whose type is the one written around it.
            MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } => model.GetEnclosingSymbol(named.SpanStart, token)?.ContainingType,
            MemberAccessExpressionSyntax access   => model.GetTypeInfo(access.Expression, token).Type,
            MemberBindingExpressionSyntax binding => binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault(conditional => conditional.WhenNotNull.Span.Contains(binding.Span)) is { } conditional
                                                         ? model.GetTypeInfo(conditional.Expression, token).Type
                                                         : null,
            SimpleNameSyntax                      => model.GetEnclosingSymbol(named.SpanStart, token)?.ContainingType,
            _                                     => null,
        };

        return receiver is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : receiver;
    }

    /// <summary>
    /// The types a receiver stands for: itself, or for a type parameter the types it is constrained to,
    /// <c>where TSender : OpenSender</c>. A cycle of constraints is the compiler's error, cut off by depth.
    /// </summary>
    private static IEnumerable<ITypeSymbol> Through(ITypeSymbol? receiver, int depth) => receiver switch
    {
        null                                      => [],
        ITypeParameterSymbol parameter when depth < 8 => parameter.ConstraintTypes.SelectMany(constraint => Through(constraint, depth + 1)),
        ITypeParameterSymbol                      => [],
        _                                         => [receiver],
    };

    /// <summary>
    /// The generic as its declaration names it: <c>Decider&lt;TCommand, TEvent&gt;</c>,
    /// <c>Runner.Run&lt;T&gt;</c>, an extension block's <c>Ex.extension&lt;T&gt;(T)</c>.
    /// </summary>
    private static string Owner(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsExtension: true } extension => $"{extension.ContainingType.Name}.{extension.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
        INamedTypeSymbol type => type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IMethodSymbol method  => $"{(method.ContainingType.IsExtension ? method.ContainingType.ContainingType : method.ContainingType).Name}.{method.Name}<{string.Join(", ", method.OriginalDefinition.TypeParameters.Select(parameter => parameter.Name))}>",
        _                     => symbol.Name,
    };

    /// <summary>What <paramref name="argument"/> breaks of what <paramref name="parameter"/> declares, as the message's last clause.</summary>
    private static IEnumerable<string> Breaches(TypeKitSymbols symbols, ITypeParameterSymbol parameter, ITypeSymbol argument, ITypeSymbol? receiver)
    {
        // Box<Missing> is the compiler's error (CS0246); what Missing would be is unknown.
        if (argument.TypeKind is TypeKind.Error) yield break;

        foreach (var constraint in Effective(symbols, parameter, receiver))
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

    /// <summary>
    /// Whether <paramref name="argument"/> meets <paramref name="constraint"/>. An array is a sealed,
    /// concrete class. An enum and a delegate are concrete, as they are sealed: an enum is a value type,
    /// which <c>new()</c> and <c>struct</c> accept, and a delegate a sealed class made from a target and
    /// a method; <c>dynamic</c> is <c>object</c>. Their bases, <c>Enum</c>, <c>Delegate</c>,
    /// <c>MulticastDelegate</c> and <c>ValueType</c>, are abstract classes.
    /// </summary>
    private static bool Satisfies(ITypeSymbol argument, TypeArgumentConstraint constraint) => constraint switch
    {
        TypeArgumentConstraint.Abstract => argument is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true, IsStatic: false },
        TypeArgumentConstraint.Sealed   => argument.TypeKind is TypeKind.Struct or TypeKind.Enum or TypeKind.Array || argument is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Delegate, IsSealed: true, IsStatic: false },
        TypeArgumentConstraint.Record   => argument is INamedTypeSymbol { IsRecord: true },
        _                               => argument.TypeKind is TypeKind.Array or TypeKind.Enum or TypeKind.Delegate or TypeKind.Dynamic
                                        || argument is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false, IsStatic: false },
    };

    /// <summary>
    /// A forwarding type parameter proves a constraint by carrying the attribute, or by C# constraints
    /// that leave it no argument that breaks it:
    /// <list type="bullet">
    /// <item><c>[Sealed]</c>: <c>struct</c> or <c>unmanaged</c>, which admit structs and enums alone; or a
    /// bound, <c>where T : U</c>, that proves it, since a sealed type has no other subtype.</item>
    /// <item><c>[Concrete]</c>: <c>new()</c>, or whatever proves <c>[Sealed]</c>, since a sealed type is
    /// never abstract.</item>
    /// <item><c>[Record]</c>: a bound that is a record, or proves it, since only records derive from
    /// records (CS8865).</item>
    /// <item><c>[Abstract]</c>: nothing but the attribute; no C# constraint excludes a concrete type.</item>
    /// </list>
    /// A cycle of constraints is the compiler's error, cut off by depth.
    /// </summary>
    private static bool Proves(TypeKitSymbols symbols, ITypeParameterSymbol forwarded, TypeArgumentConstraint constraint, int depth = 0)
    {
        if (Effective(symbols, forwarded, receiver: null).Contains(constraint)) return true;
        if (depth >= 8) return false;

        return constraint switch
        {
            TypeArgumentConstraint.Sealed   => forwarded.HasValueTypeConstraint
                                            || forwarded.ConstraintTypes.OfType<ITypeParameterSymbol>().Any(bound => Proves(symbols, bound, TypeArgumentConstraint.Sealed, depth + 1)),
            TypeArgumentConstraint.Concrete => forwarded.HasConstructorConstraint
                                            || Proves(symbols, forwarded, TypeArgumentConstraint.Sealed, depth + 1),
            TypeArgumentConstraint.Record   => forwarded.ConstraintTypes.Any(bound => bound is INamedTypeSymbol { IsRecord: true }
                                                                                   || bound is ITypeParameterSymbol parameter && Proves(symbols, parameter, TypeArgumentConstraint.Record, depth + 1)),
            _                               => false,
        };
    }

    /// <summary>
    /// The constraints a type parameter declares, with those of the parameter it implements or
    /// overrides: <c>Sender.Send&lt;T&gt;</c> implementing <c>ISender.Send&lt;[Concrete] T&gt;</c> carries
    /// <c>[Concrete]</c> whether or not it repeats the attribute, so a call through the class is held
    /// to it. Without this, the requirement ended at the interface. A method a base class declares
    /// implements an interface for a derived class that lists it, so the type the method is reached
    /// through, <paramref name="receiver"/>, is asked too. Each parameter is asked once: a default
    /// interface method reached through a derived interface is its own implementation there, and the
    /// walk recursed until the compiler's stack overflowed.
    /// </summary>
    private static IEnumerable<TypeArgumentConstraint> Effective(TypeKitSymbols symbols, ITypeParameterSymbol parameter, ITypeSymbol? receiver) =>
        Effective(symbols, parameter, receiver, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default)).Distinct();

    private static IEnumerable<TypeArgumentConstraint> Effective(TypeKitSymbols symbols, ITypeParameterSymbol parameter, ITypeSymbol? receiver, HashSet<ITypeParameterSymbol> visited)
    {
        if (!visited.Add(parameter.OriginalDefinition)) return [];

        var own = symbols.ConstraintsOn(parameter);
        if (parameter.DeclaringMethod is not { } method) return own;

        return own.Concat(Implemented(method.OriginalDefinition, receiver)
                              .Where(implemented => parameter.Ordinal < implemented.OriginalDefinition.TypeParameters.Length)
                              .SelectMany(implemented => Effective(symbols, implemented.OriginalDefinition.TypeParameters[parameter.Ordinal], receiver, visited)))
                  .ToList();
    }

    /// <summary>
    /// The methods <paramref name="method"/> overrides or implements, explicitly or implicitly, by its
    /// declaration or for <paramref name="receiver"/>; a static method implements a static abstract one.
    /// Never the method itself, which a default interface method is for an interface that inherits it.
    /// </summary>
    private static IEnumerable<IMethodSymbol> Implemented(IMethodSymbol method, ITypeSymbol? receiver)
    {
        if (method.OverriddenMethod is { } overridden) yield return overridden;

        foreach (var explicitly in method.ExplicitInterfaceImplementations) yield return explicitly;

        if (method.ExplicitInterfaceImplementations.Length > 0 || method.DeclaredAccessibility != Accessibility.Public) yield break;

        foreach (var type in Through(receiver, depth: 0).Prepend(method.ContainingType).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default))
        {
            foreach (var contract in type.AllInterfaces)
            {
                foreach (var member in contract.GetMembers(method.Name).OfType<IMethodSymbol>())
                {
                    if (member.IsGenericMethod && member.IsStatic == method.IsStatic
                     && !SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, method)
                     && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member)?.OriginalDefinition, method))
                        yield return member;
                }
            }
        }
    }
}
