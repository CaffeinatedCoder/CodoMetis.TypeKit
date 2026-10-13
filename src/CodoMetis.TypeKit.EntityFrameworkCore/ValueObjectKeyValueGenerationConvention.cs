using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>Adds <see cref="ValueObjectKeyValueGenerationConvention"/> to every model <c>UseTypeKit()</c> builds.</summary>
internal sealed class ValueObjectKeyConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var finalizing = conventionSet.ModelFinalizingConventions;

        // After the last of EF's finalizing conventions that change what its value-generation convention
        // re-reads, and so before a provider's value-generation strategy (an identity column on PostgreSQL
        // and SQL Server), which is chosen for keys already generated on add and comes after them.
        finalizing.Insert(IndexAfterTheLastReset(finalizing), new ValueObjectKeyValueGenerationConvention());

        // Last of all: a TPC key is generated only where the provider gave it a sequence.
        finalizing.Add(new ValueObjectTpcKeyValueGenerationConvention());

        return conventionSet;
    }

    /// <summary>
    /// EF's value-generation convention decides a key again, from its CLR type, whenever an entity type's
    /// mapping strategy or table changes (<c>RelationalValueGenerationConvention.ProcessEntityTypeAnnotationChanged</c>),
    /// and two finalizing conventions change them: <see cref="EntityTypeHierarchyMappingConvention"/> sets
    /// the mapping strategy on every hierarchy's root, TPH where none is configured, and
    /// <see cref="SharedTableConvention"/> makes table names unique. Decided before them, the key of a TPH
    /// root went back to never generated on SQLite, whose provider reads the CLR type, and the second
    /// entity added threw for a duplicate key (measured with EF Core 10.0.12).
    /// </summary>
    private static int IndexAfterTheLastReset(List<IModelFinalizingConvention> finalizing)
    {
        var last = finalizing.FindLastIndex(convention => convention is EntityTypeHierarchyMappingConvention or SharedTableConvention);

        return last + 1;
    }
}

/// <summary>
/// A single-column primary key that is a value object over an integer is generated on add, as a key
/// of that integer type is: EF's own convention looks at the property's CLR type and leaves every
/// value object alone, so <c>OrderNo : IValue&lt;int&gt;</c> lost the identity column <c>int</c> had,
/// and an entity added without a key was stored as 0.
/// </summary>
/// <remarks>
/// <para>
/// The integer types are those EF generates a key of (<see cref="IsGeneratedByTheDatabase"/>), so a
/// value-object key follows what EF does for the type it wraps. A value object over a
/// <see cref="Guid"/> is left as EF leaves it, never generated: an identifier the application
/// creates itself (<c>OrderId.New()</c>) is then always inserted, whereas a generated key that is set
/// makes EF take a new entity reached through a navigation for an existing one.
/// </para>
/// <para>
/// It sets the convention's value only, so <c>ValueGeneratedNever()</c> or any other explicit
/// configuration still wins, and a key that is also a foreign key is left alone.
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
    /// EF Core 10): every integer type but <see cref="byte"/>, the unsigned ones, <see cref="sbyte"/> and
    /// <see cref="char"/> included.
    /// </summary>
    private static bool IsGeneratedByTheDatabase(Type wrapped) =>
        wrapped == typeof(int) || wrapped == typeof(long) || wrapped == typeof(short) || wrapped == typeof(sbyte)
     || wrapped == typeof(uint) || wrapped == typeof(ulong) || wrapped == typeof(ushort) || wrapped == typeof(char);
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
