using System;
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
/// <c>[Abstract]</c>, <c>[Sealed]</c>, <c>[Record]</c>, <c>[Concrete]</c>, <c>[Interface]</c>, <c>[Unsealed]</c>,
/// <c>[ValueObject]</c>, <c>[ClosedSet]</c>, <c>[FlagsEnum]</c> or <c>[ReadOnlyStruct]</c>, or a pair of
/// arguments that breaks what two parameters declare together, <c>[Distinct]</c> or <c>[Unrelated]</c>, at
/// any closed use.
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
/// <para>
/// <c>[Distinct]</c> and <c>[Unrelated]</c> are relational: every pair of parameters of one generic that
/// both carry one is checked at the stricter level either declares, and a breach is reported once, at the
/// later argument. Two types are the same when they are one symbol, nullable annotations aside, and
/// related when an implicit identity, reference or boxing conversion runs either way. A type parameter
/// given as an argument is told apart from the other argument when both are parameters of one generic
/// carrying the attribute, or when their C# constraints admit no common (or no related) type.
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
        description: "A type parameter marked [Abstract], [Sealed], [Record], [Concrete], [Interface], [Unsealed], [ValueObject], [ClosedSet], [FlagsEnum] or [ReadOnlyStruct] constrains its arguments where C# cannot, and two marked [Distinct] or [Unrelated] constrain their arguments against each other. A closed use with an argument that breaks the constraint compiles; this rule is its error.",
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

        foreach (var diagnostic in PairDiagnostics(symbols, (CSharpCompilation)context.Compilation, arguments, receiver, later => name.TypeArgumentList.Arguments[later].GetLocation()))
            context.Report(diagnostic);
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

        foreach (var diagnostic in PairDiagnostics(symbols, (CSharpCompilation)context.Compilation, arguments, receiver, _ => reported.GetLocation()))
            context.Report(diagnostic);
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
            // Relational constraints are checked against the other arguments, in PairDiagnostics.
            if (constraint is TypeArgumentConstraint.Distinct or TypeArgumentConstraint.Unrelated) continue;

            if (argument is ITypeParameterSymbol forwarded)
            {
                if (!Proves(symbols, forwarded, constraint))
                    yield return $"is a type parameter that does not carry [{constraint}]";
            }
            else if (!Satisfies(symbols, argument, constraint))
            {
                yield return constraint switch
                {
                    TypeArgumentConstraint.Abstract       => "is not an abstract class or record",
                    TypeArgumentConstraint.Sealed         => "is not sealed",
                    TypeArgumentConstraint.Record         => "is not a record",
                    TypeArgumentConstraint.Interface      => "is not an interface",
                    TypeArgumentConstraint.Unsealed       => "is not a class that can be derived from",
                    TypeArgumentConstraint.ValueObject    => "is not a value object (IValue<T>)",
                    TypeArgumentConstraint.ClosedSet      => "is not a closed set (IClosedSet<TSelf>)",
                    TypeArgumentConstraint.FlagsEnum      => "is not an enum marked [Flags]",
                    TypeArgumentConstraint.ReadOnlyStruct => "is not a readonly struct",
                    _                                     => "is not a concrete class, record or struct",
                };
            }
        }
    }

    /// <summary>
    /// One diagnostic per pair of arguments of one generic that breaks <c>[Distinct]</c> or <c>[Unrelated]</c>,
    /// at the stricter level either parameter declares, reported at the later argument of the pair
    /// (<paramref name="locationOf"/> its index), so a breach is one report and not two. A member's list
    /// holds its extension block's arguments and its own, two generics: only arguments of one generic are a pair.
    /// </summary>
    private static IEnumerable<Diagnostic> PairDiagnostics(TypeKitSymbols symbols, CSharpCompilation compilation, List<TypeArgument> arguments, ITypeSymbol? receiver, Func<int, Location> locationOf)
    {
        if (arguments.Count < 2) yield break;

        var levels = arguments.Select(argument => Level(Effective(symbols, argument.Parameter, receiver))).ToList();
        if (levels.All(level => level is TypeArgumentConstraint.Abstract)) yield break;

        for (var later = 1; later < arguments.Count; later++)
        {
            if (levels[later] is TypeArgumentConstraint.Abstract) continue;

            for (var earlier = 0; earlier < later; earlier++)
            {
                if (levels[earlier] is TypeArgumentConstraint.Abstract) continue;
                if (!SymbolEqualityComparer.Default.Equals(arguments[earlier].Owner, arguments[later].Owner)) continue;

                var level = levels[earlier] is TypeArgumentConstraint.Unrelated || levels[later] is TypeArgumentConstraint.Unrelated
                    ? TypeArgumentConstraint.Unrelated
                    : TypeArgumentConstraint.Distinct;

                if (PairBreach(symbols, compilation, arguments[earlier], arguments[later], level) is { } breach)
                {
                    yield return Diagnostic.Create(
                        Rule,
                        locationOf(later),
                        arguments[later].Argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        arguments[later].Parameter.Name,
                        Owner(arguments[later].Owner),
                        breach);
                }
            }
        }
    }

    /// <summary>The relational level a parameter's constraints declare: <c>Unrelated</c>, <c>Distinct</c>, or <c>Abstract</c> standing for none.</summary>
    private static TypeArgumentConstraint Level(IEnumerable<TypeArgumentConstraint> constraints)
    {
        var level = TypeArgumentConstraint.Abstract;

        foreach (var constraint in constraints)
        {
            if (constraint is TypeArgumentConstraint.Unrelated) return constraint;
            if (constraint is TypeArgumentConstraint.Distinct) level = constraint;
        }

        return level;
    }

    /// <summary>
    /// What the pair breaks at <paramref name="level"/>, as the message's last clause on the later
    /// argument, or <see langword="null"/>. Two closed types are compared as they are; a type parameter, or a
    /// type built from one, <c>List&lt;T&gt;</c> or <c>T[]</c>, is a breach unless it is proved apart
    /// (<see cref="ProvedDistinct"/>, <see cref="ProvedUnrelated"/>), since otherwise the requirement would end
    /// at the first forwarding parameter. Compared as written, <c>List&lt;T&gt;</c> and <c>List&lt;int&gt;</c>
    /// were two types, and <c>Listed&lt;int&gt;</c> held one twice (reported 2026-10-14).
    /// </summary>
    private static string? PairBreach(TypeKitSymbols symbols, CSharpCompilation compilation, TypeArgument earlier, TypeArgument later, TypeArgumentConstraint level)
    {
        var (first, second) = (earlier.Argument, later.Argument);
        if (first.TypeKind is TypeKind.Error || second.TypeKind is TypeKind.Error) return null;

        var other = $"'{first.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}', the type argument for '{earlier.Parameter.Name}'";

        if (Open(first) || Open(second))
        {
            if (Same(compilation, first, second)) return $"is the same type as the argument for '{earlier.Parameter.Name}'";

            return level switch
            {
                TypeArgumentConstraint.Unrelated when !ProvedUnrelated(symbols, compilation, first, second) => $"is not proved unrelated to {other}",
                TypeArgumentConstraint.Distinct when !ProvedDistinct(symbols, compilation, first, second)   => $"is not proved a different type from {other}",
                _                                                                                            => null,
            };
        }

        if (Same(compilation, first, second)) return $"is the same type as the argument for '{earlier.Parameter.Name}'";

        return level is TypeArgumentConstraint.Unrelated && Related(compilation, first, second)
            ? $"derives from, implements, boxes to or is a base of {other}"
            : null;
    }

    /// <summary>
    /// One symbol, nullable annotations aside, or an identity conversion between the two: <c>dynamic</c> is
    /// <c>object</c>, and a tuple is the tuple whatever its element names.
    /// </summary>
    private static bool Same(CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second) =>
        SymbolEqualityComparer.Default.Equals(first, second) || compilation.ClassifyConversion(first, second).IsIdentity;

    /// <summary>
    /// Whether an implicit identity, reference or boxing conversion runs either way: a base and a derived
    /// type, an interface and an implementer, <c>object</c> and anything, a struct and an interface it
    /// implements. A numeric, nullable or user-defined conversion is no relation.
    /// </summary>
    private static bool Related(CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second) =>
        Converts(compilation, first, second) || Converts(compilation, second, first);

    private static bool Converts(CSharpCompilation compilation, ITypeSymbol from, ITypeSymbol to)
    {
        var conversion = compilation.ClassifyConversion(from, to);
        return conversion.IsImplicit && (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);
    }

    /// <summary>
    /// Whether two arguments, at least one a type parameter or built from one, are proved different types:
    /// no closed use can make them one (<see cref="CouldBeSame"/>).
    /// </summary>
    private static bool ProvedDistinct(TypeKitSymbols symbols, CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second) =>
        !CouldBeSame(symbols, compilation, first, second);

    /// <summary>
    /// Whether some closed use could make the two types one. Two type parameters could, unless they are
    /// proved apart (<see cref="ParametersApart"/>); a type parameter could be a type that meets its C#
    /// constraints (<see cref="CouldBe"/>), but never one built from itself, <c>T</c> and <c>List&lt;T&gt;</c>;
    /// two arrays could if their ranks match and their elements could; two generic types could if they have
    /// one definition and each pair of arguments could; anything else built from a parameter is taken as
    /// possible. Each position is asked apart, so <c>KeyValuePair&lt;T, T&gt;</c> and
    /// <c>KeyValuePair&lt;int, string&gt;</c> could be one, which errs toward a report.
    /// </summary>
    private static bool CouldBeSame(TypeKitSymbols symbols, CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second) => (first, second) switch
    {
        (ITypeParameterSymbol one, ITypeParameterSymbol two) => SymbolEqualityComparer.Default.Equals(one, two) || !ParametersApart(symbols, compilation, one, two),
        (ITypeParameterSymbol parameter, var type)           => !Mentions(type, parameter) && CouldBe(compilation, parameter, type),
        (var type, ITypeParameterSymbol parameter)           => !Mentions(type, parameter) && CouldBe(compilation, parameter, type),
        (IArrayTypeSymbol one, IArrayTypeSymbol two)         => one.Rank == two.Rank && CouldBeSame(symbols, compilation, one.ElementType, two.ElementType),
        (IArrayTypeSymbol, INamedTypeSymbol) or (INamedTypeSymbol, IArrayTypeSymbol) => false,
        (INamedTypeSymbol one, INamedTypeSymbol two) when Open(one) || Open(two) =>
            SymbolEqualityComparer.Default.Equals(one.OriginalDefinition, two.OriginalDefinition)
         && AllArguments(one).Zip(AllArguments(two), (x, y) => CouldBeSame(symbols, compilation, x, y)).All(could => could),
        _ => Open(first) || Open(second) || Same(compilation, first, second),
    };

    /// <summary>The type arguments of a generic type and of the types it is nested in, outermost first.</summary>
    private static IEnumerable<ITypeSymbol> AllArguments(INamedTypeSymbol type) =>
        (type.ContainingType is { } outer ? AllArguments(outer) : []).Concat(type.TypeArguments);

    /// <summary>Whether <paramref name="type"/> is built from <paramref name="parameter"/>: no finite type is both, so they are never one.</summary>
    private static bool Mentions(ITypeSymbol type, ITypeParameterSymbol parameter) => type switch
    {
        ITypeParameterSymbol other => SymbolEqualityComparer.Default.Equals(other, parameter),
        IArrayTypeSymbol array     => Mentions(array.ElementType, parameter),
        INamedTypeSymbol named     => named.TypeArguments.Any(argument => Mentions(argument, parameter)) || named.ContainingType is { } outer && Mentions(outer, parameter),
        _                          => false,
    };

    /// <summary>
    /// Whether two type parameters are proved different types: both carry <c>[Distinct]</c> or
    /// <c>[Unrelated]</c> in one generic, whose closed uses are checked in turn; or their C# constraints admit
    /// no common type, <c>struct</c> against <c>class</c> or a class bound no value type boxes to, or two class
    /// bounds neither deriving from the other. An <c>Enum</c> bound admits every enum, which <c>struct</c>
    /// admits too: taken as proof, two <c>DayOfWeek</c> passed through a forwarding generic (reported 2026-10-14).
    /// </summary>
    private static bool ParametersApart(TypeKitSymbols symbols, CSharpCompilation compilation, ITypeParameterSymbol one, ITypeParameterSymbol two) =>
        Together(symbols, one, two, TypeArgumentConstraint.Distinct)
     || (IsValueTypes(one) && (two.HasReferenceTypeConstraint || ClassBound(two) is { } bound && NoBox(bound)))
     || (IsValueTypes(two) && (one.HasReferenceTypeConstraint || ClassBound(one) is { } bound_ && NoBox(bound_)))
     || (ClassBound(one) is { } first_ && ClassBound(two) is { } second_ && DifferentChains(compilation, first_, second_));

    /// <summary>
    /// Whether two arguments, at least one a type parameter or built from one, are proved unrelated: two
    /// parameters of one generic both carrying <c>[Unrelated]</c>; a <c>struct</c> parameter against a class
    /// other than <c>object</c>, <c>ValueType</c> or <c>Enum</c>, which a value type boxes to, or against a
    /// parameter bound to such a class; or two class bounds neither deriving from the other, since a class
    /// has one base chain. <c>class</c> alone proves nothing: <c>object</c> satisfies it and is related to
    /// everything. Two types built from parameters, neither one, are proved by <see cref="BuiltApart"/>.
    /// </summary>
    private static bool ProvedUnrelated(TypeKitSymbols symbols, CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second)
    {
        if (first is ITypeParameterSymbol one && second is ITypeParameterSymbol two)
        {
            if (Together(symbols, one, two, TypeArgumentConstraint.Unrelated)) return true;

            return (IsValueTypes(one) && ClassBound(two) is { } bound && NoBox(bound))
                || (IsValueTypes(two) && ClassBound(one) is { } bound_ && NoBox(bound_))
                || (ClassBound(one) is { } first_ && ClassBound(two) is { } second_ && DifferentChains(compilation, first_, second_));
        }

        if (first is not ITypeParameterSymbol && second is not ITypeParameterSymbol) return BuiltApart(symbols, compilation, first, second);

        var (parameter, type) = first is ITypeParameterSymbol p ? (p, second) : ((ITypeParameterSymbol)second, first);

        if (IsValueTypes(parameter)) return type.TypeKind is TypeKind.Class or TypeKind.Delegate && NoBox(type);

        return ClassBound(parameter) is { } classBound
            && type.TypeKind is not TypeKind.Interface and not TypeKind.Dynamic and not TypeKind.TypeParameter
            && type.SpecialType is not SpecialType.System_Object
            && DifferentChains(compilation, classBound, type);
    }

    /// <summary>
    /// Whether no type derives from both classes, nor one from the other: a class has one base chain, so two
    /// classes meet only where one derives from the other. A class built from a type parameter,
    /// <c>Node&lt;T&gt;</c>, is compared by its definition, since its arguments are decided at each closed use:
    /// compared as written, <c>where T : Node&lt;T&gt;</c> was proved apart from <c>Leaf : Node&lt;Leaf&gt;</c>,
    /// and so were two parameters bound to <c>Node&lt;TA&gt;</c> and <c>Node&lt;TB&gt;</c> (reported 2026-10-14).
    /// </summary>
    private static bool DifferentChains(CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second) =>
        Open(first) || Open(second)
            ? !Derives(first, second) && !Derives(second, first)
            : !Related(compilation, first, second);

    /// <summary>Whether <paramref name="from"/>'s definition is <paramref name="type"/>'s or one of its bases'.</summary>
    private static bool Derives(ITypeSymbol type, ITypeSymbol from)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, from.OriginalDefinition)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is or is built from a type parameter, <c>T</c>, <c>List&lt;T&gt;</c>,
    /// <c>T[]</c>, <c>Outer&lt;T&gt;.Inner</c>: what it stands for is decided at each closed use.
    /// </summary>
    private static bool Open(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol               => true,
        IArrayTypeSymbol array             => Open(array.ElementType),
        IPointerTypeSymbol pointer         => Open(pointer.PointedAtType),
        IFunctionPointerTypeSymbol pointer => Open(pointer.Signature.ReturnType) || pointer.Signature.Parameters.Any(parameter => Open(parameter.Type)),
        INamedTypeSymbol named             => named.TypeArguments.Any(Open) || named.ContainingType is { } outer && Open(outer),
        _                                  => false,
    };

    /// <summary>
    /// Whether two types, neither a type parameter and one built from one, are unrelated at every closed use:
    /// two value types that are never one, since a value type converts to another by identity alone; a value
    /// type and a class, an array or a delegate no value type boxes to; or two of a class, an array and a
    /// delegate of different chains, compared by their definitions. Two arrays and two delegates are related
    /// by variance beyond their definitions, and an interface by whatever implements it: they prove nothing.
    /// </summary>
    private static bool BuiltApart(TypeKitSymbols symbols, CSharpCompilation compilation, ITypeSymbol first, ITypeSymbol second)
    {
        static bool Value(ITypeSymbol type)   => type.TypeKind is TypeKind.Struct or TypeKind.Enum;
        static bool Chained(ITypeSymbol type) => type.TypeKind is TypeKind.Class or TypeKind.Array or TypeKind.Delegate;

        if (Value(first) && Value(second)) return !CouldBeSame(symbols, compilation, first, second);
        if (Value(first)) return Chained(second) && NoBox(second);
        if (Value(second)) return Chained(first) && NoBox(first);

        return Chained(first) && Chained(second)
            && !(first.TypeKind == second.TypeKind && first.TypeKind is TypeKind.Array or TypeKind.Delegate)
            && DifferentChains(compilation, first, second);
    }

    /// <summary>Whether both parameters carry <paramref name="constraint"/> (or a stricter one) in one generic, so its closed uses are checked for the pair.</summary>
    private static bool Together(TypeKitSymbols symbols, ITypeParameterSymbol one, ITypeParameterSymbol two, TypeArgumentConstraint constraint)
    {
        if (!SymbolEqualityComparer.Default.Equals(Declarer(one), Declarer(two))) return false;

        var first  = Level(Effective(symbols, one, receiver: null));
        var second = Level(Effective(symbols, two, receiver: null));

        return constraint is TypeArgumentConstraint.Unrelated
            ? first is TypeArgumentConstraint.Unrelated && second is TypeArgumentConstraint.Unrelated
            : first is not TypeArgumentConstraint.Abstract && second is not TypeArgumentConstraint.Abstract;
    }

    private static ISymbol? Declarer(ITypeParameterSymbol parameter) => (ISymbol?)parameter.DeclaringMethod?.OriginalDefinition ?? parameter.DeclaringType?.OriginalDefinition;

    /// <summary><c>struct</c> or <c>unmanaged</c>: value types alone, which never derive from a class.</summary>
    private static bool IsValueTypes(ITypeParameterSymbol parameter) => parameter.HasValueTypeConstraint || parameter.HasUnmanagedTypeConstraint;

    /// <summary>The class a parameter is bound to, <c>where T : Handler</c>, other than <c>object</c>; an interface bound is none.</summary>
    private static INamedTypeSymbol? ClassBound(ITypeParameterSymbol parameter) =>
        parameter.ConstraintTypes.OfType<INamedTypeSymbol>().FirstOrDefault(bound => bound.TypeKind is TypeKind.Class && bound.SpecialType is not SpecialType.System_Object);

    /// <summary>A class no value type boxes to: not <c>object</c>, <c>ValueType</c> or <c>Enum</c>.</summary>
    private static bool NoBox(ITypeSymbol type) => type.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum);

    /// <summary>
    /// Whether <paramref name="type"/> could be the argument for <paramref name="parameter"/>: it meets the
    /// parameter's C# constraints as far as they can be read, <c>struct</c>, <c>unmanaged</c>, <c>class</c>
    /// and each bound that is a closed type. A <c>new()</c> constraint and a bound that is itself a parameter
    /// are taken as met, and so is an interface bound built from one: <c>where T : IEquatable&lt;T&gt;</c> is met
    /// by <c>int</c>, which implements <c>IEquatable&lt;int&gt;</c> and not the bound as written, and was taken
    /// as proof that <c>T</c> is never <c>int</c> (reported 2026-10-14). A class bound built from one,
    /// <c>Node&lt;T&gt;</c>, is met by a type whose chain holds its definition. A type built from a parameter,
    /// <c>List&lt;U&gt;</c>, is read the same way against every bound, and as unmanaged, since its arguments
    /// decide both.
    /// </summary>
    private static bool CouldBe(CSharpCompilation compilation, ITypeParameterSymbol parameter, ITypeSymbol type)
    {
        if (parameter.HasValueTypeConstraint && !(type.IsValueType && type.OriginalDefinition.SpecialType is not SpecialType.System_Nullable_T)) return false;
        if (parameter.HasUnmanagedTypeConstraint && !Open(type) && !type.IsUnmanagedType) return false;
        if (parameter.HasReferenceTypeConstraint && !type.IsReferenceType) return false;

        return parameter.ConstraintTypes.OfType<INamedTypeSymbol>().All(bound => Open(bound) || Open(type)
                                                                                   ? bound.TypeKind is not TypeKind.Class || Derives(type, bound)
                                                                                   : Converts(compilation, type, bound));
    }

    /// <summary>
    /// Whether <paramref name="argument"/> meets <paramref name="constraint"/>. An array is a sealed,
    /// concrete class. An enum and a delegate are concrete, as they are sealed: an enum is a value type,
    /// which <c>new()</c> and <c>struct</c> accept, and a delegate a sealed class made from a target and
    /// a method; <c>dynamic</c> is <c>object</c>. Their bases, <c>Enum</c>, <c>Delegate</c>,
    /// <c>MulticastDelegate</c> and <c>ValueType</c>, are abstract classes. Unsealed is a class that can
    /// be derived from, abstract included, an interface not; a value object and a closed set are what the
    /// marker interfaces say, through any interface; a readonly struct is the declaration, which an enum is not.
    /// </summary>
    private static bool Satisfies(TypeKitSymbols symbols, ITypeSymbol argument, TypeArgumentConstraint constraint) => constraint switch
    {
        TypeArgumentConstraint.Abstract       => argument is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: true, IsStatic: false },
        TypeArgumentConstraint.Sealed         => argument.TypeKind is TypeKind.Struct or TypeKind.Enum or TypeKind.Array || argument is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Delegate, IsSealed: true, IsStatic: false },
        TypeArgumentConstraint.Record         => argument is INamedTypeSymbol { IsRecord: true },
        TypeArgumentConstraint.Interface      => argument.TypeKind is TypeKind.Interface,
        TypeArgumentConstraint.Unsealed       => argument is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: false, IsStatic: false },
        TypeArgumentConstraint.ValueObject    => symbols.FindMarker(argument, out _) is not null,
        TypeArgumentConstraint.ClosedSet      => symbols.FindClosedSetMarker(argument) is not null,
        TypeArgumentConstraint.FlagsEnum      => symbols.IsFlagsEnum(argument),
        TypeArgumentConstraint.ReadOnlyStruct => argument is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsReadOnly: true },
        _                                     => argument.TypeKind is TypeKind.Array or TypeKind.Enum or TypeKind.Delegate or TypeKind.Dynamic
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
    /// <item><c>[ValueObject]</c>, <c>[ClosedSet]</c>: a bound that is or implements the marker interface, or
    /// a bound parameter that proves it, which <c>FindMarker</c> reads from the constraints.</item>
    /// <item><c>[Abstract]</c>, <c>[Interface]</c>, <c>[Unsealed]</c>, <c>[FlagsEnum]</c>, <c>[ReadOnlyStruct]</c>:
    /// nothing but the attribute; no C# constraint excludes a concrete type, a class, a sealed class, a plain
    /// enum or a mutable struct.</item>
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
            TypeArgumentConstraint.ValueObject => symbols.FindMarker(forwarded, out _) is not null,
            TypeArgumentConstraint.ClosedSet   => symbols.FindClosedSetMarker(forwarded) is not null,
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
