using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Rewrites <c>.Value</c> read through the value-object interface, as a generic method or base class reads it, to
/// the unwrap the translators know (<see cref="ValueThroughInterface"/>), before EF translates the query.
/// </summary>
/// <remarks>
/// <c>QueryCompilationStarting</c> sees the LINQ tree once per query shape, after EF has evaluated what reads no
/// database, so what is left to rewrite reads a column. It does not see the query filters EF adds later, which
/// <see cref="ValueThroughInterfaceQueryFilterConvention"/> rewrites.
/// </remarks>
internal sealed class ValueThroughInterfaceInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        ValueThroughInterface.Rewrite(queryExpression);
}

/// <summary>Rewrites the same in the model's query filters, named ones included, when the model is built.</summary>
/// <remarks>
/// EF adds an entity type's query filters to a query after <c>QueryCompilationStarting</c>, so
/// <see cref="ValueThroughInterfaceInterceptor"/> never sees them. A compiled model writes the rewritten filter out,
/// which calls a public method.
/// </remarks>
internal sealed class ValueThroughInterfaceQueryFilterConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            foreach (var filter in entityType.GetDeclaredQueryFilters().ToList())
            {
                if (filter.Expression is not { } expression || ValueThroughInterface.Rewrite(expression) is not LambdaExpression rewritten || rewritten == expression)
                    continue;

                if (filter.IsAnonymous) entityType.SetQueryFilter(rewritten);
                else entityType.SetQueryFilter(filter.Key, rewritten);
            }
    }
}

/// <summary>
/// <c>.Value</c> read through <see cref="IValueObject{TSelf,T}"/>, rewritten to
/// <see cref="ValueObjectConverter{TValueObject,T}.ProviderValue"/>, which the method translator turns into the
/// column, as it does <c>GetValue()</c>.
/// </summary>
/// <remarks>
/// <para>
/// In a generic repository or base class, <c>where T : IValueObject&lt;T, string&gt;</c>, C# writes
/// <c>r.Name.Value</c> as <c>((IValueObject&lt;Name, string&gt;)r.Name).Value</c>: the interface's member, on a
/// conversion EF cannot translate, so the query failed with "could not be translated" before any translator saw
/// <c>.Value</c>, for a record class and a record struct alike (measured with EF Core 10.0.12 on SQLite).
/// </para>
/// <para>
/// Not to the value object's own <c>Value</c> property: finding it from a <see cref="Type"/> is a member lookup the
/// trimmer may break (IL2075). The unwrap is <see cref="ConverterMethods"/>', reached with the value object as a type
/// argument, so nothing is looked up by name and nothing is made with <c>MakeGenericType</c>.
/// </para>
/// <para>
/// Only the conversion of a value object to its own interface is rewritten: the interface's type argument is the
/// operand's type, which <see cref="ValueObjectTypes"/> knows as a value object wrapping what <c>Value</c> returns.
/// Anything else, an operand already typed as the interface for one, is left to EF.
/// </para>
/// </remarks>
internal static class ValueThroughInterface
{
    public static Expression Rewrite(Expression expression) => Rewriter.Instance.Visit(expression);

    private sealed class Rewriter : ExpressionVisitor
    {
        public static readonly Rewriter Instance = new();

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node is { Member: PropertyInfo { Name: nameof(IValueObject<,>.Value) } property, Expression: UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion }
             && property.DeclaringType is { IsGenericType: true } contract
             && contract.GetGenericTypeDefinition() == typeof(IValueObject<,>)
             && conversion.Type == contract
             && contract.GetGenericArguments()[0] == conversion.Operand.Type
             && ValueObjectTypes.Describe(conversion.Operand.Type) is { } valueObject
             && valueObject.WrappedType == property.PropertyType)
                return Expression.Call(ConverterMethods.ProviderValue(valueObject), Visit(conversion.Operand));

            return base.VisitMember(node);
        }
    }
}
