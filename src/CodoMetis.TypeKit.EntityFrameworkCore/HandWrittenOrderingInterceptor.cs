using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Refuses a query that compares, orders, or takes the minimum or maximum of a value object whose
/// ordering is hand-written (<see cref="HandWrittenOrderingAttribute"/>): the database orders its column
/// by the wrapped value, and <c>CompareTo</c> cannot run there.
/// </summary>
/// <remarks>
/// <para>
/// EF translates <c>o.Priority &gt; p</c> as <c>"o"."Priority" &gt; @p</c>, dropping the operator the
/// value object declares, rewrites <c>o.Priority.CompareTo(p) &gt; 0</c> the same way, and orders by the
/// column: with a descending <c>CompareTo</c>, every answer was the opposite of the in-memory one,
/// without a word (measured with EF Core 10.0.12 on SQLite). Comparing and ordering by <c>.Value</c>
/// stays allowed: that says the wrapped value's order is meant.
/// </para>
/// <para>
/// An interceptor because no translator sees these: EF translates a comparison without asking for the
/// operator's method, and an ordering key is a column, not a call. <c>QueryCompilationStarting</c> sees
/// the LINQ tree once per query shape, before any translation, with the value objects' types intact;
/// a precompiled query (Native AOT) passes through it at <c>dbcontext optimize</c>, which then fails.
/// It does not see the query filters EF adds later, which <see cref="HandWrittenOrderingQueryFilterConvention"/> refuses.
/// </para>
/// </remarks>
internal sealed class HandWrittenOrderingInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        HandWrittenOrdering.RefuseIn(queryExpression);

        return queryExpression;
    }
}

/// <summary>
/// Refuses a query filter that compares, orders, or takes the minimum or maximum of a value object whose
/// ordering is hand-written, when the model is built, named filters included.
/// </summary>
/// <remarks>
/// EF adds an entity type's query filters to a query after <c>QueryCompilationStarting</c>, so
/// <see cref="HandWrittenOrderingInterceptor"/> never saw them: <c>HasQueryFilter(r =&gt; r.Rank &gt; From(2))</c>
/// kept the rows the column's order keeps, the opposite of the in-memory one for a descending
/// <c>CompareTo</c> (measured with EF Core 10.0.12 on SQLite). A filter is part of the model, and applies
/// to every query of its entity type and of the navigations that reach it, so it is refused where the
/// model is built, as an invalid configuration is; a compiled model is refused at <c>dbcontext optimize</c>.
/// </remarks>
internal sealed class HandWrittenOrderingQueryFilterConvention : IModelFinalizedConvention
{
    public IModel ProcessModelFinalized(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
            foreach (var filter in entityType.GetDeclaredQueryFilters())
                if (filter.Expression is { } expression)
                    HandWrittenOrdering.RefuseInFilter(expression, filter.IsAnonymous
                        ? $"in the query filter of {entityType.DisplayName()}"
                        : $"in the query filter \"{filter.Key}\" of {entityType.DisplayName()}");

        return model;
    }
}

/// <summary>
/// Finds a comparison, an ordering, or a minimum or maximum of a value object with a hand-written ordering, and throws:
/// in C#, or through the database's own <c>EF.Functions.Greatest</c>, <c>Least</c> and row-value comparisons.
/// </summary>
internal static class HandWrittenOrdering
{
    /// <summary>In a query's LINQ tree, which EF has already stripped of what it evaluates in memory.</summary>
    public static void RefuseIn(Expression query) =>
        new Finder("in a query", ", or do it in memory after the query", skipsWhatRunsInMemory: false).Visit(query);

    /// <summary>
    /// In a query filter, where EF has not yet evaluated what reads nothing of the database: a comparison of
    /// the context's own values runs in memory, by <c>CompareTo</c>, and is not refused.
    /// </summary>
    public static void RefuseInFilter(LambdaExpression filter, string where) =>
        new Finder(where, "", skipsWhatRunsInMemory: true).Visit(filter);

    private sealed class Finder(string where, string orElse, bool skipsWhatRunsInMemory) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
             && !RunsInMemory(node))
            {
                Refuse(node.Left.Type, $"compared with {Operator(node.NodeType)}");
                Refuse(node.Right.Type, $"compared with {Operator(node.NodeType)}");
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;

            if (method.Name is nameof(IComparable.CompareTo) or nameof(IComparer<>.Compare))
            {
                if (!RunsInMemory(node))
                {
                    if (node.Object is not null) Refuse(node.Object.Type, $"compared with {method.Name}");
                    foreach (var argument in node.Arguments) Refuse(argument.Type, $"compared with {method.Name}");
                }
            }
            else if (IsLinq(method) && OrderedType(method) is { } ordered && !OrderingRunsInMemory(node))
            {
                Refuse(ordered, $"ordered by {method.Name}");

                // A key the selector converts (to object, IComparable) is still ordered in memory by the
                // value object's CompareTo, while EF drops the conversion and orders by the column. So are
                // elements converted before the operator, Select(r => (object)r.Rank).Max(), whose key is the
                // element itself or which has no key (#206), and a key that reads the boxed value back: through a ??,
                // a ?:, a member of a projection, a grouping's key or elements, a join, or a subquery's First.
                var key = KeySelector(node);
                IEnumerable<Type> read = key is not null ? ReadTypes(key.Body, Bound(node, _scope))
                                       : method.IsGenericMethod ? ElementTypes(node.Arguments[0], _scope, method.GetGenericArguments()[0])
                                       : [];
                foreach (var type in read) Refuse(type, $"ordered by {method.Name}");
            }
            else if (IsDbFunction(method) && !RunsInMemory(node))
            {
                // EF.Functions.Greatest and Least (relational: GREATEST, LEAST, SQLite's max and min) pick by the
                // column's order, as Max does.
                if (method.Name is "Greatest" or "Least" && method.IsGenericMethod)
                    Refuse(method.GetGenericArguments()[0], $"compared by {method.Name}");

                // A row-value comparison (Npgsql's EF.Functions.GreaterThan((a, b), (x, y)) and its siblings) compares
                // each element by its column's order; its tuples arrive boxed to ITuple.
                if (method.Name is "GreaterThan" or "GreaterThanOrEqual" or "LessThan" or "LessThanOrEqual")
                    foreach (var argument in node.Arguments)
                        if (argument.Type == typeof(ITuple))
                            foreach (var element in TupleElements(Unconverted(argument).Type))
                                Refuse(element, $"compared in a row value by {method.Name}");
            }

            // The operator's lambdas are visited knowing what their parameters stand for, so that an operator inside
            // one, g.Max() in Select(g => g.Max()), reads the elements its source was built from.
            var outer = _scope;
            _scope = Bound(node, outer);
            try
            {
                return base.VisitMethodCall(node);
            }
            finally
            {
                _scope = outer;
            }
        }

        /// <summary>What the parameters of the lambdas around the node being visited stand for.</summary>
        private Scope _scope = Scope.Empty;

        /// <summary>A function of <c>EF.Functions</c>: an extension of <see cref="DbFunctions"/>, whichever provider declares it.</summary>
        private static bool IsDbFunction(MethodInfo method) =>
            method.IsStatic && method.GetParameters() is [{ ParameterType: var functions }, ..] && functions == typeof(DbFunctions);

        /// <summary>The element types of a tuple type, a long tuple's rest included; none for a type that is no generic tuple.</summary>
        private static IEnumerable<Type> TupleElements(Type tuple)
        {
            if (!tuple.IsGenericType || !typeof(ITuple).IsAssignableFrom(tuple)) yield break;

            foreach (var element in tuple.GetGenericArguments())
            {
                yield return element;
                foreach (var nested in TupleElements(element)) yield return nested;
            }
        }

        /// <summary>The operator's key or value selector, quoted for <c>Queryable</c>, a lambda for <c>Enumerable</c>.</summary>
        private static LambdaExpression? KeySelector(MethodCallExpression node) =>
            node.Arguments.Count > 1 ? StripQuotes(node.Arguments[1]) as LambdaExpression : null;

        private static Expression StripQuotes(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? StripQuotes(quote.Operand) : expression;

        /// <summary>What an expression of the query stands for, as far as the query says: a value, or a sequence.</summary>
        private abstract record Meaning;

        /// <summary>An expression, read where its lambdas' parameters stand for what <see cref="Scope"/> says.</summary>
        private sealed record Value(Expression Expression, Scope Scope) : Meaning;

        /// <summary>A grouping (its keys, and its elements), or with no keys, a sequence a lambda is given whole.</summary>
        private sealed record Group(IReadOnlyList<Meaning> Keys, IReadOnlyList<Meaning> Elements) : Meaning;

        /// <summary>What each lambda parameter in reach stands for; a parameter it does not name is read as its own type.</summary>
        private sealed class Scope(ImmutableDictionary<ParameterExpression, IReadOnlyList<Meaning>> bound)
        {
            public static readonly Scope Empty = new(ImmutableDictionary<ParameterExpression, IReadOnlyList<Meaning>>.Empty);

            public Scope With(ParameterExpression parameter, IReadOnlyList<Meaning> meanings) => new(bound.SetItem(parameter, meanings));

            public IReadOnlyList<Meaning> Of(ParameterExpression parameter) => bound.TryGetValue(parameter, out var meanings) ? meanings : [];
        }

        /// <summary>
        /// <paramref name="scope"/> with the parameters of a LINQ operator's lambdas bound: to the elements of the source
        /// for a selector, key or predicate; for <c>Join</c> and <c>GroupJoin</c>, each side's to its sequence; for
        /// <c>SelectMany</c>'s result selector, the collection's elements; for <c>GroupBy</c>'s, the key and the elements.
        /// </summary>
        private static Scope Bound(MethodCallExpression call, Scope scope)
        {
            if (!IsLinq(call.Method) || !call.Method.IsGenericMethod || call.Method.Name is nameof(Enumerable.Aggregate)) return scope;

            var lambdas = Lambdas(call);
            if (lambdas.Count == 0) return scope;

            var source = Elements(call.Arguments[0], scope);
            switch (call.Method.Name)
            {
                case nameof(Queryable.Join) or nameof(Queryable.GroupJoin) when lambdas is [var outerKey, var innerKey, var result]:
                    var inner = Elements(call.Arguments[1], scope);
                    return scope.With(outerKey.Parameters[0], source).With(innerKey.Parameters[0], inner).With(result.Parameters[0], source)
                                .With(result.Parameters[1], call.Method.Name is nameof(Queryable.Join) ? inner : [new Group([], inner)]);
                case nameof(Queryable.SelectMany):
                    var collection = scope.With(lambdas[0].Parameters[0], source);
                    return lambdas is [var selector, var resultSelector]
                        ? collection.With(resultSelector.Parameters[0], source).With(resultSelector.Parameters[1], Elements(selector.Body, collection))
                        : collection;
                case nameof(Queryable.GroupBy):
                    var (keys, elements) = Grouping(lambdas, scope, source);
                    var grouped = lambdas.Where(lambda => lambda.Parameters.Count == 1).Aggregate(scope, (bound, lambda) => bound.With(lambda.Parameters[0], source));
                    return lambdas.FirstOrDefault(lambda => lambda.Parameters.Count == 2) is { } resultOf
                        ? grouped.With(resultOf.Parameters[0], keys).With(resultOf.Parameters[1], [new Group([], elements)])
                        : grouped;
                default:
                    return lambdas.Aggregate(scope, (bound, lambda) => bound.With(lambda.Parameters[0], source));
            }
        }

        /// <summary>A <c>GroupBy</c>'s keys, its key selector's body, and its elements, its element selector's body or the source's.</summary>
        private static (IReadOnlyList<Meaning> Keys, IReadOnlyList<Meaning> Elements) Grouping(IReadOnlyList<LambdaExpression> lambdas, Scope scope, IReadOnlyList<Meaning> source)
        {
            var selectors = lambdas.Where(lambda => lambda.Parameters.Count == 1).ToList();
            Meaning Read(LambdaExpression selector) => new Value(selector.Body, scope.With(selector.Parameters[0], source));

            return ([Read(selectors[0])], selectors is [_, var element, ..] ? [Read(element)] : source);
        }

        private static List<LambdaExpression> Lambdas(MethodCallExpression call) => [.. call.Arguments.Select(StripQuotes).OfType<LambdaExpression>()];

        /// <summary>
        /// What a sequence's elements are, as far as the query says: a <c>Select</c>'s body, a join's or a <c>GroupBy</c>'s
        /// result, a grouping, <c>SelectMany</c>'s collection, followed through the operators that pass the elements on,
        /// and the elements of a grouping or a sequence a lambda was given; none for a sequence the query does not build.
        /// </summary>
        private static IReadOnlyList<Meaning> Elements(Expression sequence, Scope scope)
        {
            switch (Unconverted(sequence))
            {
                case ParameterExpression parameter:
                    return [.. scope.Of(parameter).OfType<Group>().SelectMany(group => group.Elements)];
                case MethodCallExpression { Method: { IsGenericMethod: true } method } call when IsLinq(method):
                    var lambdas = Lambdas(call);
                    switch (method.Name)
                    {
                        case nameof(Queryable.Select) when lambdas is [var selector]:
                            return [new Value(selector.Body, Bound(call, scope))];
                        case nameof(Queryable.SelectMany) when lambdas is [var selector]:
                            return Elements(selector.Body, Bound(call, scope));
                        case nameof(Queryable.SelectMany) or nameof(Queryable.Join) or nameof(Queryable.GroupJoin) when lambdas is [.., { Parameters.Count: 2 } result]:
                            return [new Value(result.Body, Bound(call, scope))];
                        case nameof(Queryable.GroupBy) when lambdas is [_, ..]:
                            if (lambdas.FirstOrDefault(lambda => lambda.Parameters.Count == 2) is { } resultOf) return [new Value(resultOf.Body, Bound(call, scope))];
                            var (keys, elements) = Grouping(lambdas, scope, Elements(call.Arguments[0], scope));
                            return [new Group(keys, elements)];
                        case nameof(Queryable.Cast) or nameof(Queryable.OfType):
                            var target = method.GetGenericArguments()[0];
                            return [.. Elements(call.Arguments[0], scope).Where(element => element is not Value value || ReadTypes(value.Expression, value.Scope).Any(target.IsAssignableFrom))];
                        default:
                            return [.. Upstream(call).SelectMany(upstream => Elements(upstream, scope))];
                    }
                default:
                    return [];
            }
        }

        /// <summary>
        /// The types a sequence's elements have under their boxing and reference conversions. <paramref name="declared"/>
        /// is the element type the consuming operator reads, for a sequence the query does not build.
        /// </summary>
        private static IEnumerable<Type> ElementTypes(Expression sequence, Scope scope, Type declared) =>
            Elements(sequence, scope) is [_, ..] elements ? elements.SelectMany(element => TypesOf(element, declared)) : [declared];

        private static IEnumerable<Type> TypesOf(Meaning meaning, Type declared) =>
            meaning is Value value ? ReadTypes(value.Expression, value.Scope) : [declared];

        /// <summary>
        /// The types an expression reads under its boxing and reference conversions: both sides of a <c>??</c> and a
        /// <c>?:</c>, what a parameter stands for, the expression a member of a projection or a grouping's key was set
        /// from, and the element a <c>First</c> and its kind take.
        /// </summary>
        private static IEnumerable<Type> ReadTypes(Expression expression, Scope scope)
        {
            switch (Unconverted(expression))
            {
                case BinaryExpression { NodeType: ExpressionType.Coalesce } coalesce:
                    return [.. ReadTypes(coalesce.Left, scope), .. ReadTypes(coalesce.Right, scope)];
                case ConditionalExpression conditional:
                    return [.. ReadTypes(conditional.IfTrue, scope), .. ReadTypes(conditional.IfFalse, scope)];
                case ParameterExpression parameter when scope.Of(parameter) is [_, ..] meanings:
                    return meanings.SelectMany(meaning => TypesOf(meaning, parameter.Type));
                case MemberExpression { Expression: { } instance } member when Members(instance, member.Member, scope) is [_, ..] meanings:
                    return meanings.SelectMany(meaning => TypesOf(meaning, member.Type));
                case MethodCallExpression call when TakesAnElement(call) && Elements(call.Arguments[0], scope) is [_, ..] elements:
                    return elements.SelectMany(element => TypesOf(element, call.Type));
                case var read:
                    return [read.Type];
            }
        }

        /// <summary>
        /// What <paramref name="member"/> of <paramref name="instance"/> was set from: an argument of a <c>new</c>, a
        /// binding of a member initializer, a grouping's key, found in the expression itself or in what it stands for.
        /// </summary>
        private static IReadOnlyList<Meaning> Members(Expression instance, MemberInfo member, Scope scope)
        {
            switch (Unconverted(instance))
            {
                case NewExpression { Members: { } members } created:
                    return [.. members.Select((declared, index) => (declared, index))
                                      .Where(pair => pair.declared.HasSameMetadataDefinitionAs(member))
                                      .Select(pair => new Value(created.Arguments[pair.index], scope))];
                case MemberInitExpression initialized:
                    return [.. initialized.Bindings.OfType<MemberAssignment>()
                                          .Where(binding => binding.Member.HasSameMetadataDefinitionAs(member))
                                          .Select(binding => new Value(binding.Expression, scope))];
                case ParameterExpression parameter:
                    return [.. scope.Of(parameter).SelectMany(meaning => MembersOf(meaning, member))];
                case MemberExpression { Expression: { } outer } nested:
                    return [.. Members(outer, nested.Member, scope).SelectMany(meaning => MembersOf(meaning, member))];
                case MethodCallExpression call when TakesAnElement(call):
                    return [.. Elements(call.Arguments[0], scope).SelectMany(element => MembersOf(element, member))];
                default:
                    return [];
            }
        }

        private static IReadOnlyList<Meaning> MembersOf(Meaning meaning, MemberInfo member) => meaning switch
        {
            Value value => Members(value.Expression, member, value.Scope),
            Group group when member is { Name: nameof(IGrouping<,>.Key), DeclaringType: { IsGenericType: true } declaring }
                          && declaring.GetGenericTypeDefinition() == typeof(IGrouping<,>) => group.Keys,
            _ => [],
        };

        /// <summary>An operator that answers one of its source's elements: <c>First</c>, <c>Single</c>, <c>Last</c>, <c>ElementAt</c> and their <c>OrDefault</c> forms.</summary>
        private static bool TakesAnElement(MethodCallExpression call) =>
            IsLinq(call.Method) && call.Method.IsGenericMethod
         && call.Method.Name is nameof(Queryable.First) or nameof(Queryable.FirstOrDefault) or nameof(Queryable.Single) or nameof(Queryable.SingleOrDefault)
                             or nameof(Queryable.Last) or nameof(Queryable.LastOrDefault) or nameof(Queryable.ElementAt) or nameof(Queryable.ElementAtOrDefault);

        /// <summary>The sequences whose elements an operator passes on as they are: its source, both for <c>Concat</c> and its kind; none for any other.</summary>
        private static Expression[] Upstream(MethodCallExpression call) => call.Method.Name switch
        {
            nameof(Queryable.AsQueryable) or nameof(Enumerable.AsEnumerable) or nameof(Queryable.Where) or nameof(Queryable.Distinct)
             or nameof(Queryable.Skip) or nameof(Queryable.Take) or nameof(Queryable.SkipWhile) or nameof(Queryable.TakeWhile)
             or nameof(Queryable.SkipLast) or nameof(Queryable.TakeLast) or nameof(Queryable.Reverse) or nameof(Queryable.DefaultIfEmpty)
             or nameof(Queryable.DistinctBy) or nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy)
             or nameof(Queryable.ThenByDescending) or nameof(Queryable.Order) or nameof(Queryable.OrderDescending) => [call.Arguments[0]],
            nameof(Queryable.Concat) or nameof(Queryable.Union) or nameof(Queryable.Intersect) or nameof(Queryable.Except) => [call.Arguments[0], call.Arguments[1]],
            _ => [],
        };

        private static bool IsLinq(MethodInfo method) => method.DeclaringType == typeof(Queryable) || method.DeclaringType == typeof(Enumerable);

        /// <summary>The expression under its boxing and reference conversions; a conversion operator, which changes the value, is kept.</summary>
        private static Expression Unconverted(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs, Method: null } conversion
                ? Unconverted(conversion.Operand)
                : expression;

        /// <summary>
        /// Whether EF evaluates the comparison before translating, in a filter: it reads no parameter
        /// bound outside it (the entity) and no query (a <c>DbSet</c> of the context).
        /// </summary>
        private bool RunsInMemory(Expression comparison) => skipsWhatRunsInMemory && !DatabaseReads.In(comparison);

        /// <summary>
        /// Whether EF evaluates an ordering before translating, in a filter: it reads nothing of the database and takes
        /// no lambda. Over the context's own values EF evaluates <c>Limits.Max()</c> and <c>Limits.Order().First()</c> in
        /// memory, by <c>CompareTo</c>, but sends <c>Limits</c> to the database as a parameter and orders it there for
        /// <c>Limits.Max(l =&gt; l)</c> or <c>Limits.OrderByDescending(l =&gt; l).First()</c>, by the column's order
        /// (measured with EF Core 10 on SQLite, #225). An ordering with a lambda that EF happens to evaluate stays refused.
        /// </summary>
        private bool OrderingRunsInMemory(Expression ordering) => RunsInMemory(ordering) && !LambdaFinder.In(ordering);

        /// <summary>The type a LINQ operator orders by: the key, the element for <c>Order</c>, the result for <c>Min</c> and <c>Max</c>.</summary>
        private static Type? OrderedType(MethodInfo method) => method.Name switch
        {
            nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending)
             or nameof(Queryable.MinBy) or nameof(Queryable.MaxBy) when method.IsGenericMethod => method.GetGenericArguments()[1],
            nameof(Queryable.Order) or nameof(Queryable.OrderDescending) when method.IsGenericMethod => method.GetGenericArguments()[0],
            nameof(Queryable.Min) or nameof(Queryable.Max) => method.ReturnType,
            _ => null,
        };

        private void Refuse(Type type, string how)
        {
            var candidate = Nullable.GetUnderlyingType(type) ?? type;

            if (!ValueObjectTypes.HasHandWrittenOrdering(candidate)) return;

            var wrapped = ValueObjectTypes.Describe(candidate)!.WrappedType;

            throw new InvalidOperationException(
                $"{candidate.Name} is {how} {where}, but its ordering is its own CompareTo, which the database cannot run: "
              + $"there the column orders by the {wrapped.Name} it holds, which need not be {candidate.Name}'s order. "
              + $"Compare or order by .Value explicitly where the {wrapped.Name}'s order is meant{orElse}.");
        }

        private static string Operator(ExpressionType type) => type switch
        {
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            _ => ">=",
        };
    }

    /// <summary>Whether an expression holds a lambda, quoted or not.</summary>
    private sealed class LambdaFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool In(Expression expression)
        {
            var finder = new LambdaFinder();
            finder.Visit(expression);

            return finder._found;
        }

        public override Expression? Visit(Expression? node) => _found || node is null ? node : base.Visit(node);

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            _found = true;

            return node;
        }
    }
}
