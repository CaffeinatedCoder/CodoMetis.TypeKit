using System.Reflection;
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
/// The translation is the operand re-typed as the wrapped type (<see cref="UnderlyingValue"/>). Only
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
            if (member.DeclaringType is not { } declaringType || !ValueObjectTypes.IsValueObject(declaringType)) return null;

            return UnderlyingValue.Of(instance, returnType, sqlExpressionFactory, typeMappingSource);
        }
    }
}

/// <summary>
/// Translates a call to a method marked <see cref="TranslatedAsUnderlyingValueAttribute"/> (the
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
            if (method.GetCustomAttribute<TranslatedAsUnderlyingValueAttribute>() is null) return null;

            var parameterType = method.GetParameters()[0].ParameterType;
            var valueType     = Nullable.GetUnderlyingType(method.ReturnType) ?? method.ReturnType;

            if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(parameterType) ?? parameterType) is not { } valueObject
             || valueObject.ValueType != valueType)
                return null;

            return UnderlyingValue.Of(arguments[0], valueType, sqlExpressionFactory, typeMappingSource);
        }
    }
}

/// <summary>A value-object operand, as the wrapped type it is stored as.</summary>
internal static class UnderlyingValue
{
    /// <summary>
    /// A column is re-typed rather than cast: it already holds the wrapped value, and a cast is not a
    /// no-op everywhere (on SQL Server, <c>CAST(Code AS nvarchar(max))</c> can stop an index seek).
    /// The mapping keeps the column's store type, so its facets survive. Any other operand, such as a
    /// parameter or a function result, is converted.
    /// </summary>
    public static SqlExpression Of(SqlExpression operand, Type valueType, ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        var mapping = (operand.TypeMapping?.StoreType is { } storeType ? typeMappingSource.FindMapping(valueType, storeType) : null)
                   ?? typeMappingSource.FindMapping(valueType);

        return operand is ColumnExpression column
                   ? new ColumnExpression(column.Name, column.TableAlias, valueType, mapping, column.IsNullable)
                   : sqlExpressionFactory.Convert(operand, valueType, mapping);
    }
}
