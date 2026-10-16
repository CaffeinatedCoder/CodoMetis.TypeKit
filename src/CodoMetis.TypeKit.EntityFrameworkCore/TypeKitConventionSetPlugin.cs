using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

/// <summary>
/// Adds TypeKit's conventions to every model <c>UseTypeKit()</c> builds: the key generation of
/// <see cref="ValueObjectKeyValueGenerationConvention"/> and <see cref="ValueObjectTpcKeyValueGenerationConvention"/>,
/// the generator of <see cref="ValueObjectValueGeneratorConvention"/>, and the refusal of <see cref="HandWrittenOrderingQueryFilterConvention"/>.
/// </summary>
internal sealed class TypeKitConventionSetPlugin : IConventionSetPlugin
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

        // After it, once whether a property is generated is final: the provider's generator for the wrapped type.
        finalizing.Add(new ValueObjectValueGeneratorConvention());

        // On the finished model, whose query filters are final.
        conventionSet.ModelFinalizedConventions.Add(new HandWrittenOrderingQueryFilterConvention());

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
