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
        // First among the finalizing conventions: a provider's value-generation strategy (an identity
        // column on PostgreSQL and SQL Server) is chosen there, for keys already generated on add.
        conventionSet.ModelFinalizingConventions.Insert(0, new ValueObjectKeyValueGenerationConvention());

        return conventionSet;
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
/// Only integers, where only the database can produce the key. A value object over a
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
        {
            if (entityType.BaseType is not null || entityType.FindPrimaryKey() is not { Properties: [var property] }) continue;
            if (property.IsForeignKey()) continue;

            if (ValueObjectTypes.Describe(Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is { WrappedType: var wrapped }
             && IsGeneratedByTheDatabase(wrapped))
                property.Builder.ValueGenerated(ValueGenerated.OnAdd);
        }
    }

    private static bool IsGeneratedByTheDatabase(Type wrapped) => wrapped == typeof(int) || wrapped == typeof(long) || wrapped == typeof(short);
}
