using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.EntityFrameworkCore;
using CodoMetis.TypeKit.Testing;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The public surface of the shipping libraries, enumerated by reflection: one key per public member declared on a
/// public type, <c>TypeName.Member</c>, nested types joined with <c>+</c> and generic arity dropped
/// (<c>Result.CatchAsync</c>, <c>FaultResponse.WithoutFaultName</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every key must be named by a claim's <c>surfaces</c> or by a waiver in <c>test/oracles/surface-waivers.json</c>,
/// so a public member cannot be added without deciding which oracle holds it, or saying why none should.
/// </para>
/// <para>
/// Overloads collapse into one key. The compiler's grouping types of C# 14 <c>extension</c> blocks are skipped, their
/// members are listed on the static class, as are explicit interface implementations and accessors. The generators
/// and the analyzers are left out: their public surface is what they generate and the diagnostics they report.
/// </para>
/// </remarks>
internal static class ApiSurface
{
    /// <summary>One type per shipping library, so a satellite that is split off is still found.</summary>
    private static readonly Type[] AssemblyMarkers =
    [
        typeof(Option<>),
        typeof(FaultResponse),
        typeof(ValueObjectConverter<,>),
        typeof(global::Serilog.TypeKitSerilogExtensions),
        typeof(TypeKitAssertionException),
    ];

    /// <summary>Every public member key, sorted ordinally.</summary>
    internal static IReadOnlyList<string> Members { get; } = Enumerate();

    /// <summary>Compiles a surface pattern: <c>*</c> matches any run of characters, everything else is literal.</summary>
    internal static Regex Pattern(string glob) =>
        new("^" + string.Join(".*", glob.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);

    /// <summary>The members a pattern names.</summary>
    internal static IReadOnlyList<string> Matching(string glob)
    {
        var pattern = Pattern(glob);
        return [.. Members.Where(member => pattern.IsMatch(member))];
    }

    private static List<string> Enumerate()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AssemblyMarkers.Select(marker => marker.Assembly).Distinct())
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Name.Contains('<') || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;

            var typeName = Name(type);
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                switch (member)
                {
                    case Type:
                        continue; // listed as its own type
                    case MethodInfo { IsSpecialName: true } accessor when !accessor.Name.StartsWith("op_", StringComparison.Ordinal):
                        continue; // property and event accessors; the property is the member
                    case MethodInfo { Name: var name } when name.Contains('.') || name.Contains('<'):
                        continue; // explicit interface implementations and compiler-generated members
                }

                keys.Add($"{typeName}.{(member is ConstructorInfo ? ".ctor" : member.Name)}");
            }
        }

        return [.. keys];
    }

    private static string Name(Type type)
    {
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        var bare = tick < 0 ? type.Name : type.Name[..tick];
        return type.DeclaringType is { } outer ? Name(outer) + "+" + bare : bare;
    }
}
