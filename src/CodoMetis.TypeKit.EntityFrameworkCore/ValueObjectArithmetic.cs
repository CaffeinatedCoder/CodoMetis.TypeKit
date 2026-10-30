using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Rewrites a value object's generated arithmetic operator to the wrapped type's arithmetic over what it wraps, and
/// refuses an operator written by hand
/// (<see cref="ValueObjectArithmetic"/>), before EF translates the query.
/// </summary>
/// <remarks>
/// <c>QueryCompilationStarting</c> sees the LINQ tree once per query shape, after EF has evaluated what reads no
/// database, so what is left reads a column. It does not see the query filters EF adds later, which
/// <see cref="ValueObjectArithmeticQueryFilterConvention"/> rewrites.
/// </remarks>
internal sealed class ValueObjectArithmeticInterceptor : IQueryExpressionInterceptor
{
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        ValueObjectArithmetic.Rewrite(queryExpression);
}

/// <summary>Rewrites and refuses the same in the model's query filters, named ones included, when the model is built.</summary>
/// <remarks>
/// EF adds an entity type's query filters to a query after <c>QueryCompilationStarting</c>, so
/// <see cref="ValueObjectArithmeticInterceptor"/> never sees them. A filter applies to every query of its entity type,
/// so a hand-written operator in one is refused where the model is built, as an invalid configuration is. A
/// compiled model writes the rewritten filter out, which calls public methods.
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

/// <summary>
/// A value object's operator in a query: its generated arithmetic translated as the wrapped type's, and one written
/// by hand refused.
/// </summary>
/// <remarks>
/// <para>
/// EF translates an operator as the database's arithmetic over the operands' columns, and never calls its method.
/// Between two value objects that is what a generated operator does, where the column holds the wrapped value
/// (<c>ef_add("p"."Price", "p"."Tax")</c> on SQLite). Where the application's own converter stores it, as
/// <c>"M:" + value</c>, it added the texts: <c>Price + Tax &gt; 4</c> kept no row where memory keeps two, and
/// <c>-Price &lt; -4</c> all three where memory keeps two. With an operand or a result of another type it applied the
/// value object's converter to the other side: <c>o.Price * 2m</c> threw "Invalid cast from 'System.Decimal' to
/// 'Money'", and <c>o.Tax / o.Price</c>, a <see cref="decimal"/>, "No coercion operator is defined between types
/// 'Money' and 'Nullable&lt;Decimal&gt;'" (measured with EF Core 10.0.12 on SQLite). Unary <c>+</c> was not
/// translated at all.
/// </para>
/// <para>
/// So every generated operator is rewritten to what it does: the wrapped type's operator over
/// <see cref="ValueObjectConverter{TValueObject,T}.ProviderValue"/> of each value-object operand, which translates
/// only where TypeKit's converter stores the column (<see cref="WrappedValue.Of"/>), the other operand
/// as it is, and a result of the value object's type wrapped by
/// <see cref="ValueObjectConverter{TValueObject,T}.Materialize"/>, which the method translator turns into the
/// wrapped type's result re-typed as the value object. Unary <c>+</c> of a number is the number. Only an operator
/// marked <see cref="TranslatedAsWrappedArithmeticAttribute"/> is: the generators mark the ones they write, which
/// call the wrapped type's through <see cref="GeneratedArithmetic"/>.
/// </para>
/// <para>
/// An operator the value object declares without the mark was written by hand, and its body is what the database
/// cannot run: <c>From(Math.Round(left.Value + right.Value, 2))</c> added the columns and never rounded, so a filter
/// kept the rows of the plain sum. It is refused, an arithmetic, bitwise or shift operator alike, naming the value
/// object and <c>.Value</c>, as a hand-written <c>CompareTo</c> is (<see cref="HandWrittenOrdering"/>). A
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
    /// context's own values runs in memory, as written, and is neither rewritten nor refused.
    /// </summary>
    public static Expression RewriteFilter(LambdaExpression filter, string where) =>
        new Rewriter(where, "", skipsWhatRunsInMemory: true).Visit(filter);

    private sealed class Rewriter(string where, string orElse, bool skipsWhatRunsInMemory) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (Operator(node.NodeType) is not { } symbol || ValueObjectOf(node.Method, node) is not { } valueObject)
                return base.VisitBinary(node);

            var left  = Visit(node.Left);
            var right = Visit(node.Right);

            var valueObjectType = valueObject.ValueObjectType;
            var leftValue  = Unwrapped(left, valueObject);
            var rightValue = Unlifted(right.Type) == valueObjectType ? Unwrapped(right, valueObject) : Unlifted(right);

            Expression result;
            try
            {
                result = Expression.MakeBinary(node.NodeType, leftValue, rightValue);
            }
            catch (InvalidOperationException)
            {
                // byte, short, char: C# computes their arithmetic as int's, and an expression tree has none of theirs.
                throw Untranslatable(valueObject, $"{valueObjectType.Name} {symbol} {Unlifted(right.Type).Name}");
            }

            return Wrapped(result, node.Type, valueObject, $"{valueObjectType.Name} {symbol} {Unlifted(right.Type).Name}");
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (Operator(node.NodeType) is not { } symbol || ValueObjectOf(node.Method, node) is not { } valueObject)
                return base.VisitUnary(node);

            var operand         = Visit(node.Operand);
            var valueObjectType = valueObject.ValueObjectType;

            if (node.NodeType == ExpressionType.UnaryPlus)
            {
                // A number's unary + is the number, which EF does not translate. Of the value object, it is the operand,
                // which reads nothing of the column: it compares or is read through its converter, whoever's it is.
                if (!IsNumber(valueObject.WrappedType)) throw Untranslatable(valueObject, $"{symbol}{valueObjectType.Name}");
                if (Unlifted(node.Type) == valueObjectType) return operand;

                return Wrapped(Unwrapped(operand, valueObject), node.Type, valueObject, $"{symbol}{valueObjectType.Name}");
            }

            var value = Unwrapped(operand, valueObject);

            return Wrapped(Expression.MakeUnary(node.NodeType, value, value.Type), node.Type, valueObject, $"{symbol}{valueObjectType.Name}");
        }

        /// <summary>
        /// The value object whose generated operator <paramref name="method"/> is, or <see langword="null"/> for any
        /// other operator and one that runs in memory; a hand-written operator of a value object is refused here.
        /// </summary>
        private GeneratedValueObjectAttribute? ValueObjectOf(MethodInfo? method, Expression node)
        {
            if (method?.DeclaringType is not { } declaringType || ValueObjectTypes.Describe(declaringType) is not { } valueObject) return null;
            if (skipsWhatRunsInMemory && !DatabaseReads.In(node)) return null;

            if (method.GetCustomAttribute<TranslatedAsWrappedArithmeticAttribute>() is null
             || method.GetParameters() is not [{ ParameterType: var first }, ..]
             || first != declaringType)
                throw new InvalidOperationException(
                    $"{declaringType.Name}'s operator {Operator(node.NodeType)} is used {where}, but it is written by hand, and the database cannot run it: "
                  + $"there it is the {valueObject.WrappedType.Name} arithmetic of the column, which need not be what the operator does. "
                  + $"Compute it from .Value where the {valueObject.WrappedType.Name} arithmetic is meant{orElse}.");

            return valueObject;
        }

        /// <summary>
        /// The wrapped type's result as the operator's: re-typed as the value object by
        /// <see cref="ValueObjectConverter{TValueObject,T}.Materialize"/>, or as it is for any other result type.
        /// </summary>
        /// <remarks>
        /// The tree is only translated, never run, so a <c>Nullable</c> is unwrapped and wrapped again by a conversion
        /// EF drops: the column's nullability carries a null through the SQL.
        /// </remarks>
        private Expression Wrapped(Expression result, Type type, GeneratedValueObjectAttribute valueObject, string what)
        {
            var valueObjectType = valueObject.ValueObjectType;
            var expected        = Unlifted(type) == valueObjectType ? valueObject.WrappedType : Unlifted(type);

            if (Unlifted(result.Type) != expected) throw Untranslatable(valueObject, what);

            var unlifted = Unlifted(result);
            var wrapped  = Unlifted(type) == valueObjectType ? Expression.Call(ConverterMethods.Materialize(valueObject), unlifted) : unlifted;

            return wrapped.Type == type ? wrapped : Expression.Convert(wrapped, type);
        }

        /// <summary>A value-object operand as <see cref="ValueObjectConverter{TValueObject,T}.ProviderValue"/> of it, which translates as its column.</summary>
        private static Expression Unwrapped(Expression operand, GeneratedValueObjectAttribute valueObject) =>
            Expression.Call(ConverterMethods.ProviderValue(valueObject), Unlifted(operand));

        private Exception Untranslatable(GeneratedValueObjectAttribute valueObject, string what) =>
            new InvalidOperationException(
                $"{what} is used {where}, but {valueObject.WrappedType.Name} has no such operator EF can translate. "
              + $"Compute it from .Value{orElse}.");
    }

    /// <summary>The operand without its <c>Nullable</c>: a conversion, which EF drops, and a null passes through the SQL.</summary>
    private static Expression Unlifted(Expression operand) =>
        Nullable.GetUnderlyingType(operand.Type) is { } underlying ? Expression.Convert(operand, underlying) : operand;

    private static Type Unlifted(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>The types whose unary <c>+</c> is the value itself.</summary>
    private static bool IsNumber(Type type) =>
        type.IsPrimitive || type == typeof(decimal) || type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128) || type == typeof(BigInteger);

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
