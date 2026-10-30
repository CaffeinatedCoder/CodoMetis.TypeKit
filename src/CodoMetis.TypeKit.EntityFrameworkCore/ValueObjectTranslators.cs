using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Translates <c>.Value</c> on a value object inside a query into the column the value object is
/// already stored as, so <c>o.Code.Value.StartsWith("A")</c> works in LINQ as it does in memory.
/// </summary>
/// <remarks>
/// The translation is the operand re-typed as the wrapped type (<see cref="WrappedValue"/>). Only
/// <c>Value</c> declared by a value object is translated; <c>Nullable&lt;T&gt;.Value</c> and every
/// other <c>Value</c> are left to EF.
/// </remarks>
internal sealed class ValueObjectMemberTranslatorPlugin(
    ISqlExpressionFactory        sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMemberTranslatorPlugin
{
    public IEnumerable<IMemberTranslator> Translators { get; } = [new Translator(sqlExpressionFactory, typeMappingSource)];

    private sealed class Translator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource) : IMemberTranslator
    {
        public SqlExpression? Translate(
            SqlExpression?                             instance,
            MemberInfo                                 member,
            Type                                       returnType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger
        )
        {
            if (instance is null || member.Name != nameof(IValueObject<,>.Value)) return null;
            if (member.DeclaringType is not { } declaringType || ValueObjectTypes.Describe(declaringType) is not { } valueObject) return null;

            return WrappedValue.Of(instance, valueObject, returnType, sqlExpressionFactory, typeMappingSource);
        }
    }
}

/// <summary>
/// Translates a call to a method marked <see cref="TranslatedAsWrappedValueAttribute"/> (the
/// generated <c>GetValue()</c> and <c>ValueOrNull()</c>) into its one argument, re-typed to the
/// wrapped type.
/// </summary>
/// <remarks>
/// This covers the optional value object: <c>o.Discount.ValueOrNull()</c> on a
/// <c>Nullable&lt;Quantity&gt;</c> is not a member access on a value object and never reaches the
/// member translator. It trusts the attribute's contract, that the method body is exactly
/// <c>value?.Value</c>, but only for a method whose signature is that unwrap: from a value object,
/// or its <c>Nullable</c>, to what it wraps. The attribute is public, and a method that carried it by
/// hand without that signature was translated as the column all the same: the length of a code
/// became <c>o."Code" = '3'</c>. EF is left to refuse such a call.
/// </remarks>
internal sealed class ValueObjectMethodCallTranslatorPlugin(
    ISqlExpressionFactory        sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new Translator(sqlExpressionFactory, typeMappingSource)];

    private sealed class Translator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource) : IMethodCallTranslator
    {
        public SqlExpression? Translate(
            SqlExpression?                             instance,
            MethodInfo                                 method,
            IReadOnlyList<SqlExpression>               arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger
        )
        {
            if (instance is not null || arguments.Count != 1) return null;

            if (WrappedResult(method) is { } wrapping)
                return WrappedValue.AsValueObject(arguments[0], wrapping, sqlExpressionFactory, typeMappingSource);

            if (method.GetCustomAttribute<TranslatedAsWrappedValueAttribute>() is null) return null;

            var parameterType = method.GetParameters()[0].ParameterType;
            var valueType     = Nullable.GetUnderlyingType(method.ReturnType) ?? method.ReturnType;

            if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(parameterType) ?? parameterType) is not { } valueObject
             || valueObject.WrappedType != valueType)
                return null;

            return WrappedValue.Of(arguments[0], valueObject, valueType, sqlExpressionFactory, typeMappingSource);
        }

        /// <summary>
        /// The value object <paramref name="method"/> is <see cref="ValueObjectConverter{TValueObject,T}.Materialize"/>
        /// of, which a generated operator's result is rewritten to (<see cref="ValueObjectArithmetic"/>); <see langword="null"/>
        /// for any other method.
        /// </summary>
        private static GeneratedValueObjectAttribute? WrappedResult(MethodInfo method) =>
            method.DeclaringType is { IsGenericType: true } converter
         && converter.GetGenericTypeDefinition() == typeof(ValueObjectConverter<,>)
         && ValueObjectTypes.Describe(converter.GetGenericArguments()[0]) is { } valueObject
         && method == ConverterMethods.Materialize(valueObject)
                ? valueObject
                : null;
    }
}

/// <summary>A value-object operand, as the wrapped type it is stored as.</summary>
internal static class WrappedValue
{
    /// <summary>
    /// A column is re-typed rather than cast: it already holds the wrapped value, and a cast is not a
    /// no-op everywhere (on SQL Server, <c>CAST(Code AS nvarchar(max))</c> can stop an index seek).
    /// The mapping keeps the column's store type, so its facets survive. So is the database's arithmetic
    /// over such columns, <c>(o.Price * 2m).Value</c>, which a cast to the column's store type would round
    /// on SQL Server. Any other operand, such as a parameter or a function's result, is converted, and so
    /// is the element column of a primitive collection (<see cref="IsCollectionElement"/>).
    /// </summary>
    /// <returns>
    /// <see langword="null"/>, so EF refuses the query, where the operand's converter is not TypeKit's:
    /// a column the application converts itself (<c>HasConversion(c => "C:" + c.Value, …)</c>, a converter
    /// type, a <c>ConfigureConventions</c> conversion) does not hold <c>.Value</c>, and re-typed as if it
    /// did, <c>t.Code.Value == "abc"</c> compared the column with what it never holds and answered no row.
    /// An operand that is not mapped yet, such as a parameter whose mapping EF infers later, is a value
    /// object of the query's own and is converted as before.
    /// </returns>
    public static SqlExpression? Of(
        SqlExpression                 operand,
        GeneratedValueObjectAttribute valueObject,
        Type                          valueType,
        ISqlExpressionFactory         sqlExpressionFactory,
        IRelationalTypeMappingSource  typeMappingSource
    )
    {
        var mapping = (operand.TypeMapping?.StoreType is { } storeType ? typeMappingSource.FindMapping(valueType, storeType) : null)
                   ?? typeMappingSource.FindMapping(valueType);

        if (!HoldsTheWrappedValue(operand, valueObject, mapping)) return null;

        return Retyped(operand, valueType, mapping, sqlExpressionFactory) ?? sqlExpressionFactory.Convert(operand, valueType, mapping);
    }

    /// <summary>
    /// Whether <paramref name="operand"/> holds the wrapped value, as a column TypeKit's converter stores does; each
    /// branch of a <c>CASE</c> or a <c>COALESCE</c> on its own, since EF gives the whole the first branch's mapping and
    /// leaves each column its own.
    /// </summary>
    private static bool HoldsTheWrappedValue(SqlExpression operand, GeneratedValueObjectAttribute valueObject, RelationalTypeMapping? mapping) =>
        Branches(operand) is { } branches
            ? branches.All(branch => HoldsTheWrappedValue(branch, valueObject, mapping))
            : operand.TypeMapping is not { } operandMapping
           || (operandMapping.Converter is { } converter && ValueObjectMapping.For(valueObject).StoresTheWrappedValue(converter, mapping?.Converter));

    /// <summary>
    /// The wrapped type's <paramref name="operand"/> as the value object over it, which is what the database holds
    /// for one: the wrapped type's arithmetic over a value object's columns, re-typed, with the value object's
    /// mapping for the same store type, so it compares with and is read as a value object.
    /// </summary>
    /// <remarks>
    /// Re-typed rather than cast, as <see cref="Of"/> re-types: a cast to the column's store type is no identity on
    /// SQL Server, where <c>CAST([p].[Price] / 3.0 AS decimal(18,2))</c> rounds what memory does not. A conversion
    /// <see cref="Of"/> made is undone; any other operand is converted.
    /// </remarks>
    public static SqlExpression AsValueObject(
        SqlExpression                 operand,
        GeneratedValueObjectAttribute valueObject,
        ISqlExpressionFactory         sqlExpressionFactory,
        IRelationalTypeMappingSource  typeMappingSource
    )
    {
        var type    = valueObject.ValueObjectType;
        var mapping = (operand.TypeMapping?.StoreType is { } storeType ? typeMappingSource.FindMapping(type, storeType) : null)
                   ?? typeMappingSource.FindMapping(type);

        if (operand is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion && conversion.Operand.Type == type) return conversion.Operand;

        return Retyped(operand, type, mapping, sqlExpressionFactory) ?? sqlExpressionFactory.Convert(operand, type, mapping);
    }

    /// <summary>
    /// <paramref name="operand"/> as <paramref name="type"/>, with <paramref name="mapping"/>, where it holds that value
    /// already: a table's column, the database's arithmetic over columns, or a <c>CASE</c> or <c>COALESCE</c> of such,
    /// each branch re-typed as it would be alone; <see langword="null"/> for anything else.
    /// </summary>
    /// <remarks>
    /// A <c>CASE</c> (<c>?:</c>) or a <c>COALESCE</c> (<c>??</c>) of the database's arithmetic was cast whole, which
    /// rounded on SQL Server: <c>(o.Optional / 32m ?? o.Price / 32m).Value</c> read 0.09 where memory reads 0.09375, a
    /// filter on it lost its row, and so did arithmetic over a conditional (measured on Azure SQL Edge). A branch
    /// that holds the value some other way, such as a parameter, is converted alone. So are a subquery of one value and
    /// the <c>MAX</c> or <c>MIN</c> it reads.
    /// </remarks>
    private static SqlExpression? Retyped(SqlExpression operand, Type type, RelationalTypeMapping? mapping, ISqlExpressionFactory sqlExpressionFactory)
    {
        SqlExpression Branch(SqlExpression branch) =>
            branch is SqlConstantExpression { Value: null }
                ? new SqlConstantExpression(null, type, mapping)
                : Retyped(branch, type, mapping, sqlExpressionFactory) ?? sqlExpressionFactory.Convert(branch, type, mapping);

        return operand switch
        {
            ColumnExpression column when !IsCollectionElement(column) => new ColumnExpression(column.Name, column.TableAlias, type, mapping, column.IsNullable),
            SqlBinaryExpression binary when IsArithmetic(binary.OperatorType) => new SqlBinaryExpression(binary.OperatorType, binary.Left, binary.Right, type, mapping),
            SqlUnaryExpression { OperatorType: ExpressionType.Negate } negation => new SqlUnaryExpression(ExpressionType.Negate, negation.Operand, type, mapping),

            // SQLite's decimal arithmetic: ef_add, ef_multiply, ef_divide, ef_mod, ef_negate.
            SqlFunctionExpression { Instance: null, Schema: null, IsNiladic: false, Name: "ef_add" or "ef_multiply" or "ef_divide" or "ef_mod" or "ef_negate" } function =>
                sqlExpressionFactory.Function(function.Name, function.Arguments, function.IsNullable, function.ArgumentsPropagateNullability, type, mapping),

            CaseExpression @case =>
                new CaseExpression(@case.Operand, [.. @case.WhenClauses.Select(clause => new CaseWhenClause(clause.Test, Branch(clause.Result)))],
                                   @case.ElseResult is { } elseResult ? Branch(elseResult) : null),

            SqlFunctionExpression { Arguments: { } arguments, ArgumentsPropagateNullability: { } propagates } coalesce when IsCoalesce(coalesce) =>
                sqlExpressionFactory.Function(coalesce.Name, [.. arguments.Select(Branch)], coalesce.IsNullable, propagates, type, mapping),

            // MAX and MIN are one of the values they read, as a COALESCE is.
            SqlFunctionExpression { Instance: null, Schema: null, Arguments: [var argument], ArgumentsPropagateNullability: { } propagates } aggregate
                when aggregate.Name.Equals("MAX", StringComparison.OrdinalIgnoreCase) || aggregate.Name.Equals("MIN", StringComparison.OrdinalIgnoreCase) =>
                sqlExpressionFactory.Function(aggregate.Name, [Branch(argument)], aggregate.IsNullable, propagates, type, mapping),

            // A subquery of one value, such as a navigation's largest quotient: cast whole, it rounded as a CASE did.
            ScalarSubqueryExpression { Subquery: { Projection: [var projection] } select } scalar
                when Retyped(projection.Expression, type, mapping, sqlExpressionFactory) is { } projected =>
                scalar.Update(select.Update(select.Tables, select.Predicate, select.GroupBy, select.Having, [projection.Update(projected)], select.Orderings, select.Offset, select.Limit)),

            _ => null,
        };
    }

    /// <summary>The results of a <c>CASE</c> and the arguments of a <c>COALESCE</c>, each of which is the whole's value; <see langword="null"/> for anything else.</summary>
    private static IEnumerable<SqlExpression>? Branches(SqlExpression operand) =>
        operand switch
        {
            CaseExpression @case => [.. @case.WhenClauses.Select(clause => clause.Result), .. @case.ElseResult is { } elseResult ? [elseResult] : Array.Empty<SqlExpression>()],
            SqlFunctionExpression { Arguments: { } arguments } coalesce when IsCoalesce(coalesce) => arguments,
            _ => null,
        };

    /// <summary><c>??</c>, which every relational provider translates as <c>COALESCE</c>.</summary>
    private static bool IsCoalesce(SqlFunctionExpression function) =>
        function is { Instance: null, Schema: null, IsNiladic: false } && string.Equals(function.Name, "COALESCE", StringComparison.OrdinalIgnoreCase);

    private static bool IsArithmetic(ExpressionType type) =>
        type is ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo;

    /// <summary>
    /// The element of a primitive collection is a column of the collection's table expression
    /// (<c>unnest</c>, <c>json_each</c>, <c>OPENJSON</c>, EF's <c>VALUES</c>), and EF infers the
    /// collection's element mapping from the columns that read it. Re-typed, the column handed it the
    /// wrapped type's mapping for a collection of value objects, and translating <c>o.Tags.Any(t =>
    /// t.Value == "x")</c> threw (measured with EF Core 10.0.12 on PostgreSQL and SQLite).
    /// </summary>
    /// <remarks>
    /// EF Core 10's column no longer knows its table, so the element column is recognised by the name
    /// every relational provider gives it, <c>value</c>. The name only decides between two correct
    /// translations: a table column that happens to be named <c>Value</c> is converted too, which is
    /// correct and at worst a cast.
    /// </remarks>
    private static bool IsCollectionElement(ColumnExpression column) => string.Equals(column.Name, "value", StringComparison.OrdinalIgnoreCase);
}
