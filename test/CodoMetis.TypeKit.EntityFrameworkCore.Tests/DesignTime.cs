using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>What <c>dotnet ef</c> does at design time, in process: its services, and the code it writes compiled back.</summary>
internal static class DesignTime
{
    /// <summary>
    /// The design-time services for a context, in the order and with the lookup of EF's
    /// DesignTimeServicesBuilder: the context, the provider's services (named by the provider assembly's
    /// attribute), then EF's own, which are only added where missing.
    /// </summary>
    public static ServiceProvider Services(DbContext db)
    {
        var services = new ServiceCollection().AddDbContextDesignTimeServices(db);
        var provider = Assembly.Load(new AssemblyName(db.GetService<IDatabaseProvider>().Name));
        var providerServices = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>().ShouldNotBeNull($"{provider} names no design-time services.");
        ((IDesignTimeServices)Activator.CreateInstance(provider.GetType(providerServices.TypeName, throwOnError: true)!)!).ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();

        return services.BuildServiceProvider();
    }

    /// <summary>The context's compiled model, as <c>dotnet ef dbcontext optimize</c> writes it, compiled and loaded.</summary>
    public static IModel CompiledModel(DbContext db)
    {
        using var designTime = Services(db);
        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType               = db.GetType(),
            ModelNamespace            = $"CompiledModel{Guid.NewGuid():N}",
            Language                  = "C#",
            UseNullableReferenceTypes = true,
        };
        var files = designTime.GetRequiredService<ICompiledModelCodeGeneratorSelector>().Select(options)
                              .GenerateModel(db.GetService<IDesignTimeModel>().Model, options);

        var instance = Compile([.. files.Select(file => file.Code)], db.GetType().Assembly)
                       .GetTypes()
                       .Single(type => type.Name == $"{db.GetType().Name}Model")
                       .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!;

        return (IModel)instance.GetValue(null)!;
    }

    public static Assembly Compile(IReadOnlyList<string> code, Assembly context)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                                                                                     .Append(context.Location)
                                                                                     .Distinct(StringComparer.Ordinal)
                                                                                     .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            $"DesignTime{Guid.NewGuid():N}",
            code.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        emitted.Success.ShouldBeTrue(
            $"The generated code does not compile:{Environment.NewLine}{string.Join(Environment.NewLine, emitted.Diagnostics)}{Environment.NewLine}{string.Join(Environment.NewLine, code)}");

        return Assembly.Load(image.ToArray());
    }
}
