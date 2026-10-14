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
            if (method.GetCustomAttribute<TranslatedAsWrappedValueAttribute>() is null) return null;

            var parameterType = method.GetParameters()[0].ParameterType;
            var valueType     = Nullable.GetUnderlyingType(method.ReturnType) ?? method.ReturnType;

            if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(parameterType) ?? parameterType) is not { } valueObject
             || valueObject.WrappedType != valueType)
                return null;

            return WrappedValue.Of(arguments[0], valueObject, valueType, sqlExpressionFactory, typeMappingSource);
        }
    }
}

/// <summary>A value-object operand, as the wrapped type it is stored as.</summary>
internal static class WrappedValue
{
    /// <summary>
    /// A column is re-typed rather than cast: it already holds the wrapped value, and a cast is not a
    /// no-op everywhere (on SQL Server, <c>CAST(Code AS nvarchar(max))</c> can stop an index seek).
    /// The mapping keeps the column's store type, so its facets survive. Any other operand, such as a
    /// parameter or a function result, is converted, and so is the element column of a primitive
    /// collection (<see cref="IsCollectionElement"/>).
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

        if (operand.TypeMapping is { } operandMapping
         && (operandMapping.Converter is not { } converter || !ValueObjectMapping.For(valueObject).StoresTheWrappedValue(converter, mapping?.Converter)))
            return null;

        return operand is ColumnExpression column && !IsCollectionElement(column)
                   ? new ColumnExpression(column.Name, column.TableAlias, valueType, mapping, column.IsNullable)
                   : sqlExpressionFactory.Convert(operand, valueType, mapping);
    }

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
