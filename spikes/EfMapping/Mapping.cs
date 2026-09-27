using System.Linq.Expressions;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>Candidate: EF's own extension point for conversions it discovers per CLR type.</summary>
public sealed class ValueObjectConverterSelector(ValueConverterSelectorDependencies dependencies) : ValueConverterSelector(dependencies)
{
    public override IEnumerable<ValueConverterInfo> Select(Type modelClrType, Type? providerClrType = null)
    {
        var model = Nullable.GetUnderlyingType(modelClrType) ?? modelClrType;

        if (ValueObjects.UnderlyingType(model) is { } valueType && (providerClrType is null || (Nullable.GetUnderlyingType(providerClrType) ?? providerClrType) == valueType))
            yield return new ValueConverterInfo(model, valueType, _ => ValueObjects.Converter(model, valueType));

        foreach (var info in base.Select(modelClrType, providerClrType))
            yield return info;
    }
}

public static class ValueObjects
{
    public static Type? UnderlyingType(Type type) =>
        type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>))?.GetGenericArguments()[1];

    public static ValueConverter Converter(Type model, Type valueType) =>
        (ValueConverter)Activator.CreateInstance(typeof(ValueObjectConverter<,>).MakeGenericType(model, valueType))!;
}

public sealed class ValueObjectConverter<TVO, T>() : ValueConverter<TVO, T>(ToProvider(), FromProvider())
    where TVO : IValueObject<TVO, T>, IValueObjectMaterializer<TVO, T>
    where T : notnull
{
    private static Expression<Func<TVO, T>> ToProvider()
    {
        var instance = Expression.Parameter(typeof(TVO), "instance");
        return Expression.Lambda<Func<TVO, T>>(Expression.Property(instance, typeof(TVO).GetProperty(nameof(IValueObject<TVO, T>.Value))!), instance);
    }

    // An expression tree cannot call a static abstract member (CS8927), so it calls a plain generic
    // method that does the constrained call.
    private static Expression<Func<T, TVO>> FromProvider() => value => Materializer.Create<TVO, T>(value);
}

public static class Materializer
{
    public static TVO Create<TVO, T>(T value) where TVO : IValueObjectMaterializer<TVO, T> where T : notnull => TVO.Materialize(value);
}
