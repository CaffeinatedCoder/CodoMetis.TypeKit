namespace Spike.Abstractions;

public interface IValueObject<in TValueObject, T> where TValueObject : IValueObject<TValueObject, T> where T : notnull
{
    T Value { get; }
}

public interface IValueWrapper<out TValueObject, in T> where TValueObject : IValueObject<TValueObject, T> where T : notnull
{
    abstract static TValueObject From(T value);
}

public interface IValue<T> where T : notnull;

public interface IValidatedValue<TValueObject, in T, TFault>
    where T : notnull where TFault : notnull
    where TValueObject : IValidatedValue<TValueObject, T, TFault>
{
    abstract static Result<TValueObject, TFault> Create(T value);
}

public readonly record struct Result<T, TFault>(bool IsOk, T? Value, TFault? Fault)
{
    public static implicit operator Result<T, TFault>(T value)      => new(true, value, default);
    public static Result<T, TFault> Fail(TFault fault)              => new(false, default, fault);
}
