using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Generates a value object's value with the generator the provider gives a property of the type it wraps,
/// configured the same way.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseTypeKit()</c> sets it on a value object over a <see cref="Guid"/> or a <see cref="string"/> that
/// EF generates on add, so it only needs to be named by the code EF's compiled model generates. PostgreSQL's
/// and SQL Server's providers choose a key's generator by the property's CLR type, not by its column: a
/// <see cref="Guid"/> key gets a version 7 Guid on PostgreSQL and a Guid sequential in SQL Server's order,
/// while a value object over one got EF's random Guid on both (measured with Npgsql 10.0.3 and EF Core
/// 10.0.12).
/// </para>
/// <para>
/// A type, not a delegate, because a compiled model can only name a factory type. It has no services, so
/// the generator asks the provider at the first value, through the context of the entity being added.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class WrappedTypeValueGeneratorFactory : ValueGeneratorFactory
{
    /// <summary>The generator of <paramref name="property"/>, a value object's.</summary>
    /// <param name="property">A value-object property generated on add.</param>
    /// <param name="typeBase">The type it is generated for.</param>
    /// <returns>A generator that answers the provider's values for the wrapped type, as the value object.</returns>
    /// <exception cref="InvalidOperationException">The property is no value object over a <see cref="Guid"/> or a <see cref="string"/>.</exception>
    public override ValueGenerator Create(IProperty property, ITypeBase typeBase)
    {
        ArgumentNullException.ThrowIfNull(property);

        return ValueObjectTypes.Describe(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is { } valueObject
            && WrappedTypeValueGenerator.Generates(valueObject.WrappedType)
                   ? new WrappedTypeValueGenerator(
                         ValueObjectMapping.For(valueObject).Converter,
                         valueObject.WrappedType == typeof(Guid) ? WrappedTypeValueGenerator.ProviderGenerator<Guid> : WrappedTypeValueGenerator.ProviderGenerator<string>)
                   : throw new InvalidOperationException(
                         $"{property.DeclaringType.DisplayName()}.{property.Name} is no value object over a Guid or a string, so it has no generator of its wrapped type's.");
    }
}

/// <summary>
/// The provider's generator for a property of the wrapped type, its values converted to the value object.
/// The provider is asked once, at the first value, about a key of the wrapped type in a model of its own,
/// built with the context's conventions.
/// </summary>
internal sealed class WrappedTypeValueGenerator(ValueConverter converter, Func<DbContext, ValueGenerator> providerGenerator) : ValueGenerator
{
    private ValueGenerator? _provider;

    /// <summary>A generator of the wrapped type's values already chosen, its values converted to the value object.</summary>
    public WrappedTypeValueGenerator(ValueConverter converter, ValueGenerator provider)
        : this(converter, _ => provider) =>
        _provider = provider;

    /// <summary>The wrapped types whose generator the providers choose by the property's CLR type.</summary>
    public static bool Generates(Type wrapped) => wrapped == typeof(Guid) || wrapped == typeof(string);

    /// <remarks>EF reads it after a value is generated (<c>ValueGenerationManager</c> in EF Core 10).</remarks>
    public override bool GeneratesTemporaryValues =>
        _provider?.GeneratesTemporaryValues
     ?? throw new InvalidOperationException("Whether a value object's generated value is temporary is known only once one is generated.");

    /// <remarks>None of EF's or the providers' generators of a Guid, a string or a temporary number makes stable values.</remarks>
    public override bool GeneratesStableValues => false;

    protected override object? NextValue(EntityEntry entry) =>
        converter.ConvertFromProvider((_provider ??= providerGenerator(entry.Context)).Next(entry));

    /// <summary>The generator the provider gives a key of <typeparamref name="T"/> generated on add.</summary>
    public static ValueGenerator ProviderGenerator<T>(DbContext context)
    {
        var modelBuilder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet(), context.GetService<ModelDependencies>());
        modelBuilder.Entity<Key<T>>().Property(key => key.Id).ValueGeneratedOnAdd();

        var model = context.GetService<IModelRuntimeInitializer>().Initialize(modelBuilder.FinalizeModel(), designTime: false, validationLogger: null);
        var entityType = model.FindEntityType(typeof(Key<T>))!;

        return context.GetService<IValueGeneratorSelector>().TrySelect(entityType.FindPrimaryKey()!.Properties[0], entityType, out var generator) && generator is not null
                   ? generator
                   : throw new InvalidOperationException($"The provider has no generator for a key of {typeof(T).Name} generated on add.");
    }

    private sealed class Key<T>
    {
        public T Id { get; set; } = default!;
    }
}
