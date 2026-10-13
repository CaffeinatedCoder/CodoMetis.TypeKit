using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
/// </para>
/// </remarks>
internal sealed class HandWrittenOrderingInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        new Finder().Visit(queryExpression);

        return queryExpression;
    }

    private sealed class Finder : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
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
                if (node.Object is not null) Refuse(node.Object.Type, $"compared with {method.Name}");
                foreach (var argument in node.Arguments) Refuse(argument.Type, $"compared with {method.Name}");
            }
            else if ((method.DeclaringType == typeof(Queryable) || method.DeclaringType == typeof(Enumerable)) && OrderedType(method) is { } ordered)
            {
                Refuse(ordered, $"ordered by {method.Name}");
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>The type a LINQ operator orders by: the key, the element for <c>Order</c>, the result for <c>Min</c> and <c>Max</c>.</summary>
        private static Type? OrderedType(MethodInfo method) => method.Name switch
        {
            nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending)
             or nameof(Queryable.MinBy) or nameof(Queryable.MaxBy) when method.IsGenericMethod => method.GetGenericArguments()[1],
            nameof(Queryable.Order) or nameof(Queryable.OrderDescending) when method.IsGenericMethod => method.GetGenericArguments()[0],
            nameof(Queryable.Min) or nameof(Queryable.Max) => method.ReturnType,
            _ => null,
        };

        private static void Refuse(Type type, string how)
        {
            var candidate = Nullable.GetUnderlyingType(type) ?? type;

            if (!ValueObjectTypes.HasHandWrittenOrdering(candidate)) return;

            var wrapped = ValueObjectTypes.Describe(candidate)!.WrappedType;

            throw new InvalidOperationException(
                $"{candidate.Name} is {how} in a query, but its ordering is its own CompareTo, which the database cannot run: "
              + $"there the column orders by the {wrapped.Name} it holds, which need not be {candidate.Name}'s order. "
              + $"Compare or order by .Value explicitly where the {wrapped.Name}'s order is meant, or do it in memory after the query.");
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
