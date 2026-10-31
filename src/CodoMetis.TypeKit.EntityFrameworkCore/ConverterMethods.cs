using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.CompilerServices;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// <see cref="ValueObjectConverter{TValueObject,T}.ProviderValue"/> and
/// <see cref="ValueObjectConverter{TValueObject,T}.Materialize"/> of one value object, which a query is rewritten to
/// call where it unwraps one or wraps the wrapped type's result in one.
/// </summary>
/// <remarks>
/// Reached through <see cref="GeneratedValueObjectAttribute.Accept{TResult}"/> with the value object as a type
/// argument, from the converter's expressions, whose methods the compiler names: nothing is looked up by name and
/// nothing is made with <c>MakeGenericType</c>, which the trimmer and Native AOT may break (IL2075, IL3050). Both
/// are public because a compiled model writes a rewritten query filter out as C# in the application's assembly.
/// </remarks>
internal static class ConverterMethods
{
    private static readonly ConcurrentDictionary<Type, (MethodInfo ProviderValue, MethodInfo Materialize)> Methods = new();

    public static MethodInfo ProviderValue(GeneratedValueObjectAttribute valueObject) => Of(valueObject).ProviderValue;

    public static MethodInfo Materialize(GeneratedValueObjectAttribute valueObject) => Of(valueObject).Materialize;

    private static (MethodInfo ProviderValue, MethodInfo Materialize) Of(GeneratedValueObjectAttribute valueObject) =>
        Methods.GetOrAdd(valueObject.ValueObjectType, static (_, description) => description.Accept(MethodsOf.Instance), valueObject);

    private sealed class MethodsOf : IValueObjectVisitor<(MethodInfo, MethodInfo)>
    {
        public static readonly MethodsOf Instance = new();

        public (MethodInfo, MethodInfo) Visit<TValueObject, T>()
            where TValueObject : IValueObject<TValueObject, T>, IValueObjectMaterializer<TValueObject, T>
            where T : notnull =>
            (((MethodCallExpression)ValueObjectConverter<TValueObject, T>.ToProvider.Body).Method,
             ((MethodCallExpression)ValueObjectConverter<TValueObject, T>.FromProvider.Body).Method);
    }
}
