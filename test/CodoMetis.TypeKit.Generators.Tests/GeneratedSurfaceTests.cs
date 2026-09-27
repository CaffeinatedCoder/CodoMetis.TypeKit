using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CodoMetis.TypeKit.Generators.Probes;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// The public surface the generators give each probe, compared with <c>GeneratedSurface.verified.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour tests check what they know to look for. This catches the rest: an aspect change
/// that adds, drops or renames a member, an interface or an attribute shows up as a diff in review
/// instead of shipping unnoticed.
/// </para>
/// <para>
/// On a mismatch the actual surface is written next to the snapshot as
/// <c>GeneratedSurface.received.txt</c>. If the change is intended, replace the verified file with it.
/// </para>
/// </remarks>
public sealed class GeneratedSurfaceTests
{
    private const string Verified = "GeneratedSurface.verified.txt";
    private const string Received = "GeneratedSurface.received.txt";

    [Fact]
    public void The_generated_surface_matches_the_snapshot()
    {
        var directory = Path.Combine(RepositoryRoot(), "test", "CodoMetis.TypeKit.Generators.Tests");
        var actual    = Render(typeof(ProbeId).Assembly);
        var expected  = File.Exists(Path.Combine(directory, Verified)) ? File.ReadAllText(Path.Combine(directory, Verified)) : "";

        if (actual == expected.ReplaceLineEndings("\n"))
        {
            File.Delete(Path.Combine(directory, Received));
            return;
        }

        File.WriteAllText(Path.Combine(directory, Received), actual);
        actual.ShouldBe(expected, $"The generated surface changed. Review {Received} and, if the change is intended, replace {Verified} with it.");
    }

    private static string Render(Assembly assembly)
    {
        var valueObjects = assembly.GetTypes()
                                   .Where(type => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueObject<,>)))
                                   .OrderBy(type => type.FullName, StringComparer.Ordinal)
                                   .ToList();

        valueObjects.Count.ShouldBeGreaterThanOrEqualTo(19);

        var text = new StringBuilder();

        foreach (var type in valueObjects)
        {
            text.Append(Display(type)).Append(type.IsPublic || type.IsNestedPublic ? "" : " (internal)").Append('\n');

            foreach (var line in Lines(type).Order(StringComparer.Ordinal))
                text.Append("  ").Append(line).Append('\n');

            foreach (var line in Companions(assembly, type).Order(StringComparer.Ordinal))
                text.Append("  extension ").Append(line).Append('\n');
        }

        return text.ToString();
    }

    private static IEnumerable<string> Lines(Type type) =>
    [
        .. type.GetInterfaces().Select(i => $"implements {Display(i)}"),
        .. type.GetCustomAttributesData().Where(IsDeclared).Select(attribute => $"attribute {Display(attribute.AttributeType)}"),
        .. type.GetNestedTypes().Select(nested => $"nested {nested.Name} : {string.Join(", ", [Display(nested.BaseType!), .. nested.GetInterfaces().Where(i => !i.IsAssignableFrom(nested.BaseType)).Select(Display)])}"),
        .. Members(type)
    ];

    /// <summary>
    /// The generated extension methods on <paramref name="valueObject"/>, found by what they extend
    /// rather than by their class's name, which is rendered: looked up by name, a renamed class
    /// rendered no companions at all instead of a diff, and an internal one was never listed.
    /// </summary>
    private static IEnumerable<string> Companions(Assembly assembly, Type valueObject) =>
        assembly.GetTypes()
                .Where(type => type.IsDefined(typeof(ExtensionAttribute), false))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.IsDefined(typeof(ExtensionAttribute), false)
                              && method.GetParameters()[0].ParameterType is var extended
                              && (extended == valueObject || Nullable.GetUnderlyingType(extended) == valueObject))
                .Select(method => $"{(method.IsPublic ? "" : "internal ")}static {method.DeclaringType!.Name}.{method.Name}({Parameters(method)}) : {Display(method.ReturnType)}");

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(flags))
            yield return $"new({Parameters(constructor)})";

        foreach (var property in type.GetProperties(flags))
            yield return $"{Static(property.GetMethod!)}{property.Name} : {Display(property.PropertyType)}";

        foreach (var method in type.GetMethods(flags).Where(method => !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal)))
            yield return $"{Static(method)}{method.Name}({Parameters(method)}) : {Display(method.ReturnType)}";
    }

    /// <summary>
    /// Leaves out the compiler's own bookkeeping (<c>[Nullable]</c>, <c>[IsReadOnly]</c>), which varies
    /// between compiler versions, but keeps <c>[CompilerGenerated]</c>, which the aspects put there.
    /// </summary>
    private static bool IsDeclared(CustomAttributeData attribute) =>
        attribute.AttributeType.Namespace != "System.Runtime.CompilerServices" || attribute.AttributeType == typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute);

    private static string Static(MethodBase method) => method.IsStatic ? "static " : "";

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter => (parameter.IsOut ? "out " : "") + Display(parameter.ParameterType)));

    private static string Display(Type type)
    {
        if (type.IsByRef) return Display(type.GetElementType()!);
        if (Nullable.GetUnderlyingType(type) is { } underlying) return $"{Display(underlying)}?";
        if (!type.IsGenericType) return type.IsNested && !type.IsGenericParameter ? $"{Display(type.DeclaringType!)}.{type.Name}" : type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx"))) return directory.FullName;
        }

        throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
    }
}
