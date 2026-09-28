using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

// Beside EF Core's own AddEntityFrameworkNpgsql and AddEntityFrameworkSqlite.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers CodoMetis.TypeKit in EF Core's internal service provider.</summary>
public static class TypeKitServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services <c>UseTypeKit()</c> adds, for an application that builds EF Core's internal
    /// service provider itself (<c>UseInternalServiceProvider</c>). Everyone else calls
    /// <c>UseTypeKit()</c>.
    /// </summary>
    /// <param name="serviceCollection">EF Core's internal services, not the application's.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddEntityFrameworkTypeKit(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // All three are plugins, which EF collects from every registration: nothing of EF's or of
        // another library's is replaced.
        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IRelationalTypeMappingSourcePlugin, ValueObjectTypeMappingSourcePlugin>()
            .TryAdd<IMemberTranslatorPlugin, ValueObjectMemberTranslatorPlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, ValueObjectMethodCallTranslatorPlugin>();

        return serviceCollection;
    }
}
