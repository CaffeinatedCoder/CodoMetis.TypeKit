using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Refuses an operator a value object declares by hand (<see cref="ValueObjectArithmetic"/>), before EF translates
/// the query.
/// </summary>
/// <remarks>
/// <c>QueryCompilationStarting</c> sees the LINQ tree once per query shape, after EF has evaluated what reads no
/// database, so what is left reads a column. It does not see the query filters EF adds later, which
/// <see cref="ValueObjectArithmeticQueryFilterConvention"/> checks.
/// </remarks>
internal sealed class ValueObjectArithmeticInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        ValueObjectArithmetic.Rewrite(queryExpression);
}

/// <summary>Refuses the same in the model's query filters, named ones included, when the model is built.</summary>
/// <remarks>
/// EF adds an entity type's query filters to a query after <c>QueryCompilationStarting</c>, so
/// <see cref="ValueObjectArithmeticInterceptor"/> never sees them. A filter applies to every query of its entity type,
/// so a hand-written operator in one is refused where the model is built, as an invalid configuration is.
/// </remarks>
internal sealed class ValueObjectArithmeticQueryFilterConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            foreach (var filter in entityType.GetDeclaredQueryFilters().ToList())
            {
                if (filter.Expression is not { } expression) continue;

                var where = filter.IsAnonymous
                    ? $"in the query filter of {entityType.DisplayName()}"
                    : $"in the query filter \"{filter.Key}\" of {entityType.DisplayName()}";

                if (ValueObjectArithmetic.RewriteFilter(expression, where) is not LambdaExpression rewritten || rewritten == expression) continue;

                if (filter.IsAnonymous) entityType.SetQueryFilter(rewritten);
                else entityType.SetQueryFilter(filter.Key, rewritten);
            }
    }
}

/// <summary>A value object's operator in a query: one written by hand refused.</summary>
/// <remarks>
/// <para>
/// EF translates an operator as the database's arithmetic over the operands' columns, and never calls its method.
/// For a generated operator that is what it does: the generators mark the ones they write
/// <see cref="TranslatedAsWrappedArithmeticAttribute"/>, and each calls the wrapped type's through
/// <see cref="GeneratedArithmetic"/>.
/// </para>
/// <para>
/// An operator the value object declares without the mark was written by hand, and its body is what the database
/// cannot run: with <c>From(Math.Round(left.Value + right.Value, 2))</c>, <c>Price + Tax &gt; 1.006</c> became
/// <c>ef_compare(ef_add("l"."Price", "l"."Tax"), @limit) &gt; 0</c> and kept no row where memory keeps one
/// (measured with EF Core 10.0.12 on SQLite). It is refused, an arithmetic, bitwise or shift operator alike, naming
/// the value object and <c>.Value</c>, as a hand-written <c>CompareTo</c> is (<see cref="HandWrittenOrdering"/>). A
/// conversion operator is left to EF: <c>implicit operator string(Name n) =&gt; n.Value</c> is common and
/// translates to what it does.
/// </para>
/// </remarks>
internal static class ValueObjectArithmetic
{
    /// <summary>In a query's LINQ tree, which EF has already stripped of what it evaluates in memory.</summary>
    public static Expression Rewrite(Expression query) =>
        new Rewriter("in a query", ", or do it in memory after the query", skipsWhatRunsInMemory: false).Visit(query);

    /// <summary>
    /// In a query filter, where EF has not yet evaluated what reads nothing of the database: an operator over the
    /// context's own values runs in memory, as written, and is not refused.
    /// </summary>
    public static Expression RewriteFilter(LambdaExpression filter, string where) =>
        new Rewriter(where, "", skipsWhatRunsInMemory: true).Visit(filter);

    private sealed class Rewriter(string where, string orElse, bool skipsWhatRunsInMemory) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (Operator(node.NodeType) is not null) Check(node.Method, node);

            return base.VisitBinary(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (Operator(node.NodeType) is not null) Check(node.Method, node);

            return base.VisitUnary(node);
        }

        /// <summary>Refuses <paramref name="method"/> where it is an operator of a value object written by hand, and reads the database.</summary>
        private void Check(MethodInfo? method, Expression node)
        {
            if (method?.DeclaringType is not { } declaringType || ValueObjectTypes.Describe(declaringType) is not { } valueObject) return;
            if (skipsWhatRunsInMemory && !DatabaseReads.In(node)) return;

            if (method.GetCustomAttribute<TranslatedAsWrappedArithmeticAttribute>() is null
             || method.GetParameters() is not [{ ParameterType: var first }, ..]
             || first != declaringType)
                throw new InvalidOperationException(
                    $"{declaringType.Name}'s operator {Operator(node.NodeType)} is used {where}, but it is written by hand, and the database cannot run it: "
                  + $"there it is the {valueObject.WrappedType.Name} arithmetic of the column, which need not be what the operator does. "
                  + $"Compute it from .Value where the {valueObject.WrappedType.Name} arithmetic is meant{orElse}.");
        }
    }

    /// <summary>
    /// The operators a value object can declare and EF translates as the database's own: arithmetic, bitwise and
    /// shift. Comparison is <see cref="HandWrittenOrdering"/>'s, equality is the record's, and a conversion is left to EF.
    /// </summary>
    private static string? Operator(ExpressionType type) => type switch
    {
        ExpressionType.Add or ExpressionType.AddChecked                                          => "+",
        ExpressionType.Subtract or ExpressionType.SubtractChecked                                => "-",
        ExpressionType.Multiply or ExpressionType.MultiplyChecked                                => "*",
        ExpressionType.Divide                                                                    => "/",
        ExpressionType.Modulo                                                                    => "%",
        ExpressionType.Negate or ExpressionType.NegateChecked                                    => "-",
        ExpressionType.UnaryPlus                                                                 => "+",
        ExpressionType.And                                                                       => "&",
        ExpressionType.Or                                                                        => "|",
        ExpressionType.ExclusiveOr                                                               => "^",
        ExpressionType.LeftShift                                                                 => "<<",
        ExpressionType.RightShift                                                                => ">>",
        ExpressionType.OnesComplement                                                            => "~",
        ExpressionType.Not                                                                       => "!",
        _                                                                                        => null,
    };
}
