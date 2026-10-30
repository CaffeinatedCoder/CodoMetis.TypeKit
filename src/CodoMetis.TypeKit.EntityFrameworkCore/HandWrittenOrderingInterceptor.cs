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
            else if ((method.DeclaringType == typeof(Queryable) || method.DeclaringType == typeof(Enumerable)) && OrderedType(method) is { } ordered)
            {
                Refuse(ordered, $"ordered by {method.Name}");

                // A key the selector converts (to object, IComparable) is still ordered in memory by the
                // value object's CompareTo, while EF drops the conversion and orders by the column.
                if (KeySelector(node) is { } key) Refuse(Unconverted(key.Body).Type, $"ordered by {method.Name}");
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

            return base.VisitMethodCall(node);
        }

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
}
