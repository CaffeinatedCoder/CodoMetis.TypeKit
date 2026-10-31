using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Analyzers;

/// <summary>
/// The LINQ joins of <c>Enumerable</c>, <c>Queryable</c> and <c>AsyncEnumerable</c>, resolved by symbol, and
/// what a call to one passes for each of its parameters. CMTK0008 reads their key selectors and CMTK0009
/// the side an outer join fills with <c>default</c>.
/// </summary>
internal static class LinqJoins
{
    /// <summary>
    /// Every match of a name counts, as in <see cref="TypeKitSymbols.Resolve"/>: the System.Linq.Async package
    /// declares an <c>AsyncEnumerable</c> of its own.
    /// </summary>
    private static readonly string[] Hosts = ["System.Linq.Enumerable", "System.Linq.Queryable", "System.Linq.AsyncEnumerable"];

    /// <summary>Every overload of the joins named <paramref name="names"/> the compilation can call, by original definition.</summary>
    public static IEnumerable<IMethodSymbol> Overloads(Compilation compilation, IReadOnlyCollection<string> names) =>
        Hosts.SelectMany(name => compilation.GetTypesByMetadataName(name))
             .SelectMany(type => names.SelectMany(name => type.GetMembers(name)))
             .OfType<IMethodSymbol>();

    /// <summary>The name a call is made by, as an extension, after <c>?.</c> or statically, or null for any other form.</summary>
    public static string? NameOf(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access   => access.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier       => identifier.Identifier.ValueText,
            _                                     => null
        };

    /// <summary>The receiver of an extension call: <c>rows</c> of <c>rows.LeftJoin(…)</c> and of <c>rows?.LeftJoin(…)</c>.</summary>
    private static ExpressionSyntax? Receiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax       => invocation.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
            _                                   => null
        };

    /// <summary>
    /// The join <paramref name="invocation"/> binds to or, where it does not bind, the candidate in
    /// <paramref name="joins"/> it would bind to, with what that join maps to.
    /// </summary>
    /// <remarks>
    /// A lambda that reads the generated <c>.Value</c> of a value object of the same project does not bind
    /// where Metalama runs analyzers, and neither does the join that takes it. Its candidates are every host's
    /// join of that name, with the parameters in the same places. A query is a sequence too, so the candidate
    /// taken is the one whose outer sequence the receiver is, the most derived of those: <c>Queryable</c>'s for
    /// an <c>IQueryable&lt;T&gt;</c>, as overload resolution would choose.
    /// </remarks>
    public static IMethodSymbol? Resolve<T>(SemanticModel model, InvocationExpressionSyntax invocation, Dictionary<IMethodSymbol, T> joins, CancellationToken cancellation, out T form, out bool bound)
    {
        var info = model.GetSymbolInfo(invocation, cancellation);

        bound = info.Symbol is not null;
        form  = default!;

        if (info.Symbol is IMethodSymbol symbol) return joins.TryGetValue(Definition(symbol), out form!) ? symbol : null;

        var candidates = info.CandidateSymbols.OfType<IMethodSymbol>().Where(candidate => joins.ContainsKey(Definition(candidate))).ToList();
        if (candidates.Count == 0) return null;

        var receivers = candidates.Where(candidate => OuterSequence(candidate) is { } sequence && Element(model, invocation, candidate, sequence.Ordinal, cancellation) is not null).ToList();
        var join      = receivers.FirstOrDefault(candidate => receivers.All(other => Derives(OuterSequence(candidate)!, OuterSequence(other)!))) ?? candidates[0];

        form = joins[Definition(join)];
        return join;
    }

    /// <summary>The <c>outer</c> parameter of a join's definition, whose type is the outer sequence.</summary>
    private static IParameterSymbol? OuterSequence(IMethodSymbol join) => Definition(join).Parameters.FirstOrDefault(parameter => parameter.Name == "outer");

    /// <summary>Whether the sequence type of <paramref name="parameter"/> is that of <paramref name="other"/> or derives from it.</summary>
    private static bool Derives(IParameterSymbol parameter, IParameterSymbol other)
    {
        var sequence = parameter.Type.OriginalDefinition;
        var target   = other.Type.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(sequence, target)
            || sequence.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target));
    }

    /// <summary>
    /// The element type of the sequence <paramref name="invocation"/> passes for the parameter of the join's
    /// definition at <paramref name="ordinal"/>, as that parameter reads it: <c>T</c> of the one
    /// <c>IEnumerable&lt;T&gt;</c>, <c>IQueryable&lt;T&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c> the argument
    /// is, implements, or is constrained to.
    /// </summary>
    public static ITypeSymbol? Element(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol join, int ordinal, CancellationToken cancellation)
    {
        if (Definition(join).Parameters[ordinal].Type is not INamedTypeSymbol { IsGenericType: true } parameter
         || Argument(invocation, join, ordinal) is not { } argument) return null;

        var sequence = parameter.OriginalDefinition;
        var type     = model.GetTypeInfo(argument, cancellation).Type;

        IEnumerable<ITypeSymbol> candidates = type switch
        {
            null                           => [],
            ITypeParameterSymbol constrained => constrained.ConstraintTypes.SelectMany(constraint => constraint.AllInterfaces.Prepend<ITypeSymbol>(constraint)),
            _                              => type.AllInterfaces.Prepend<ITypeSymbol>(type)
        };

        var matches = candidates.OfType<INamedTypeSymbol>()
                                .Where(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, sequence))
                                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                                .ToList();

        return matches.Count == 1 ? matches[0].TypeArguments[0] : null;
    }

    /// <summary>The unreduced original definition of a join, which <see cref="Overloads"/> lists.</summary>
    public static IMethodSymbol Definition(IMethodSymbol join) => (join.ReducedFrom ?? join).OriginalDefinition;

    /// <summary>
    /// What <paramref name="invocation"/> passes for the parameter of the join's definition at
    /// <paramref name="ordinal"/>, by position or by name: <c>outer.Join(inner, …)</c> passes the outer
    /// sequence as the receiver, so its first argument is the second parameter.
    /// </summary>
    public static ExpressionSyntax? Argument(InvocationExpressionSyntax invocation, IMethodSymbol join, int ordinal)
    {
        var definition = Definition(join);
        var offset     = join.ReducedFrom is null ? 0 : 1;

        if (offset == 1 && ordinal == 0) return Receiver(invocation);

        var arguments = invocation.ArgumentList.Arguments;

        for (var index = 0; index < arguments.Count; index++)
        {
            var at = arguments[index].NameColon is { } name
                         ? definition.Parameters.FirstOrDefault(parameter => parameter.Name == name.Name.Identifier.ValueText)?.Ordinal
                         : index + offset;

            if (at == ordinal) return arguments[index].Expression;
        }

        return null;
    }
}
