using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodoMetis.TypeKit.EntityFrameworkCore;

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

        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IMemberTranslatorPlugin, ValueObjectMemberTranslatorPlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, ValueObjectMethodCallTranslatorPlugin>();

        // Replace, not TryAdd: before the provider's services are added this registers the selector
        // first, and their TryAdd then keeps it; after them it swaps out EF's default.
        serviceCollection.Replace(ServiceDescriptor.Singleton<IValueConverterSelector, ValueObjectConverterSelector>());

        return serviceCollection;
    }
}
