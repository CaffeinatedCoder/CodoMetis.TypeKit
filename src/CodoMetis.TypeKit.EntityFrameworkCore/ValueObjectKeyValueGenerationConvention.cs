using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// A single-column primary key that is a value object over an integer is declared generated on add, as
/// a key of that integer type is: EF's own convention looks at the property's CLR type and leaves every
/// value object alone, so <c>OrderNo : IValue&lt;int&gt;</c> lost the identity column <c>int</c> had,
/// and an entity added without a key was stored as 0.
/// </summary>
/// <remarks>
/// <para>
/// The integer types are those EF declares a key of generated (<see cref="IsGeneratedByTheDatabase"/>),
/// with one exception, <see cref="char"/>, which is the application's to assign. This declares the key
/// generated; whether a value is then generated is the provider's to decide, as for a plain key (an
/// identity, a sequence), and EF picks the generator that fills a value object's key at <c>Add</c> by
/// the column's type, not by the wrapped type. So the rule is narrower than parity with the wrapped type
/// (decisions 58 and 63):
/// </para>
/// <list type="bullet">
/// <item><description>
/// A key over <see cref="char"/> is never generated. No provider generates one (PostgreSQL refuses an
/// identity on <c>character(1)</c>, SQLite an <c>AUTOINCREMENT</c> on <c>TEXT</c>, and SQL Server has no
/// identity for <c>nvarchar(1)</c>), and on SQL Server a value object's key declared generated was given
/// a character of a GUID string at <c>Add</c>, which was then stored. A default <c>'\0'</c> is stored as
/// it is: nothing checks that the application set a meaningful character.
/// </description></item>
/// <item><description>
/// A key over <see cref="ulong"/> stays declared generated, as a plain one is. Where the column is a
/// <c>decimal</c> (SQL Server, PostgreSQL), its temporary value at <c>Add</c> is a <see cref="ulong"/>,
/// from the factory its converter's mapping hints carry (<see cref="ValueObjectMapping"/>, decision
/// 114), except in a <c>--nativeaot</c> compiled model, where it still throws <see cref="OverflowException"/>.
/// </description></item>
/// </list>
/// <para>
/// A value object over a <see cref="Guid"/> is left as EF leaves it, never generated: an identifier the
/// application creates itself (<c>OrderId.New()</c>) is then always inserted, whereas a generated key
/// that is set makes EF take a new entity reached through a navigation for an existing one.
/// </para>
/// <para>
/// It sets the convention's value only, so <c>ValueGeneratedNever()</c>, <c>ValueGeneratedOnAdd()</c>, a
/// value generator or any other explicit configuration still wins, and a key that is also a foreign key
/// is left alone.
/// </para>
/// </remarks>
internal sealed class ValueObjectKeyValueGenerationConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            if (GeneratedKey(entityType) is { } property)
                property.Builder.ValueGenerated(ValueGenerated.OnAdd);
    }

    /// <summary>The entity type's key, where it is a value object over an integer EF generates a key of.</summary>
    internal static IConventionProperty? GeneratedKey(IConventionEntityType entityType)
    {
        if (entityType.BaseType is not null || entityType.FindPrimaryKey() is not { Properties: [var property] }) return null;
        // As EF's own rule: the foreign key that links a TPT table to its base type's is no reason not to
        // generate the key, and EntityTypeHierarchyMappingConvention has added it by now.
        if (property.GetContainingForeignKeys().Any(foreignKey => !foreignKey.IsBaseLinking())) return null;

        return ValueObjectTypes.Describe(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is { WrappedType: var wrapped }
            && IsGeneratedByTheDatabase(wrapped)
                   ? property
                   : null;
    }

    /// <summary>
    /// EF's own rule for a key of the wrapped type (<c>ValueGenerationConvention.CanBeGenerated</c> in
    /// EF Core 10), every integer type but <see cref="byte"/>, the unsigned ones and <see cref="sbyte"/>
    /// included, without <see cref="char"/>, which EF counts as one and no provider generates.
    /// </summary>
    private static bool IsGeneratedByTheDatabase(Type wrapped) =>
        wrapped == typeof(int) || wrapped == typeof(long) || wrapped == typeof(short) || wrapped == typeof(sbyte)
     || wrapped == typeof(uint) || wrapped == typeof(ulong) || wrapped == typeof(ushort);
}

/// <summary>
/// A TPC hierarchy's key is generated only where the provider generates it from a sequence, as EF
/// decides for an integer key: each table of the hierarchy would otherwise number its own rows, and
/// two entities of different types would share a key.
/// </summary>
/// <remarks>
/// <para>
/// EF refuses to generate an integer key under TPC (<c>RelationalValueGenerationConvention.MappingStrategyAllowsValueGeneration</c>)
/// unless the provider gives it a sequence, which PostgreSQL's and SQL Server's do, as the column's
/// default (<c>nextval</c>, <c>NEXT VALUE FOR</c>), and SQLite's does not. That default is what this
/// reads, after every provider convention, so no provider is named.
/// </para>
/// <para>
/// It reads it in the first table the key is mapped to, as the providers' own value-generation
/// conventions do (<c>GetMappedStoreObjects</c>): an abstract root has no table of its own, only its
/// derived types', and reading the root's own table found none and took a sequence's generation away
/// (measured with Npgsql 10.0.3: every entity was stored with 0).
/// </para>
/// </remarks>
internal sealed class ValueObjectTpcKeyValueGenerationConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.GetMappingStrategy() != RelationalAnnotationNames.TpcMappingStrategy) continue;
            if (ValueObjectKeyValueGenerationConvention.GeneratedKey(entityType) is not { } property) continue;
            if (property.GetValueGeneratedConfigurationSource() != ConfigurationSource.Convention) continue;

            var table = property.GetMappedStoreObjects(StoreObjectType.Table).FirstOrDefault();
            if (table.Name is null || RelationalValueGenerationConvention.GetValueGenerated(property, table) is null)
                property.Builder.ValueGenerated(null);
        }
    }
}

/// <summary>
/// A value object over a <see cref="Guid"/> or a <see cref="string"/> that EF generates on add gets its values
/// from the generator the provider gives a property of that type (<see cref="WrappedTypeValueGeneratorFactory"/>).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL's and SQL Server's providers choose a generated property's generator by its CLR type
/// (<c>FindForType</c> of <c>NpgsqlValueGeneratorSelector</c> and <c>SqlServerValueGeneratorSelector</c>), and
/// for any other type fall back to EF's, through the column's converter. So <c>ValueGeneratedOnAdd()</c> on a key
/// over a <see cref="Guid"/> gave the value object a random version 4 Guid where the plain key gets a version 7
/// one on PostgreSQL and one sequential in SQL Server's order, and a key over a <see cref="string"/> a random
/// Guid's text where PostgreSQL gives the plain key a version 7 one. The other wrapped types' generators are
/// chosen through the converter's provider type, a sequence's (HiLo) included, as for the plain type.
/// </para>
/// <para>
/// A property with a default or computed SQL value is left alone: both keys then get EF's temporary value,
/// which the database replaces. It sets the convention's value only, so a generator or a factory configured
/// on the property still wins.
/// </para>
/// </remarks>
internal sealed class ValueObjectValueGeneratorConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        foreach (var property in entityType.GetDeclaredProperties())
        {
            if (property.ValueGenerated == ValueGenerated.Never || property.GetValueGeneratorFactory() is not null) continue;
            if (property.GetDefaultValueSql() is not null || property.GetComputedColumnSql() is not null) continue;

            if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is { WrappedType: var wrapped }
             && WrappedTypeValueGenerator.Generates(wrapped))
                property.Builder.HasValueGeneratorFactory(typeof(WrappedTypeValueGeneratorFactory));
        }
    }
}
