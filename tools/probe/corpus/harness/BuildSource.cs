// Source text for the build family's probes (C17, C18, C19): a row's shape.* axes written as C#, for a compilation the probe
// makes itself (an analyzer run, or the consumer build of C18). Each piece names its types after a suffix, so the pieces of
// several rows can sit in one compilation.
namespace Probes.Build;

public static class Src
{
    public const string Usings =
        """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Threading.Tasks;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ClosedSets;
        using CodoMetis.TypeKit.ValueObjects;

        """;

    /// <summary>shape.site: <paramref name="inner"/> declared at the top level, nested in a class or a struct, two levels down, or in a generic type.</summary>
    public static string AtSite(string site, string id, string inner) => site switch
    {
        "top-level" => inner,
        "nested-in-class" => $"public static partial class O{id}\n{{\n{inner}\n}}",
        "nested-in-struct" => $"public partial struct O{id}\n{{\n{inner}\n}}",
        "nested-two-levels" => $"public static partial class O{id}\n{{\npublic sealed partial class P{id}\n{{\n{inner}\n}}\n}}",
        "in-generic-type" => $"public static partial class O{id}<TO>\n{{\n{inner}\n}}",
        _ => throw new ArgumentException("shape.site " + site),
    };

    /// <summary>shape.wrapped as a type name; enum, flags-enum and own-record-class are declared by <see cref="WrappedDeclarations"/>.</summary>
    public static string Wrapped(string wrapped, string id) => wrapped switch
    {
        "string" or "int" or "long" or "byte" or "ulong" or "decimal" or "double" or "bool" or "char" => wrapped,
        "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan" or "Uri" => "System." + wrapped,
        "enum" => $"WEnum{id}", "flags-enum" => $"WFlags{id}", "NodaTime.Instant" => "global::NodaTime.Instant", "own-record-class" => $"WRec{id}",
        _ => throw new ArgumentException("shape.wrapped " + wrapped),
    };

    public static string WrappedDeclarations(string id) =>
        $"public enum WEnum{id} {{ A, B }}\n[Flags] public enum WFlags{id} {{ None = 0, P = 1, Q = 2 }}\npublic sealed record WRec{id} {{ public int N {{ get; init; }} }}\n";

    public static bool IsStructKind(string kind) => kind is "record-struct" or "validated-struct";

    /// <summary>The type head of shape.containing, and the type as written from inside it (generic-type is closed over int outside).</summary>
    public static string ContainerHead(string containing, string name, string primary = "") => containing switch
    {
        "class" => $"public sealed partial class {name}{primary}",
        "record-class" => $"public sealed partial record {name}{primary}",
        "struct" => $"public partial struct {name}{primary}",
        "record-struct" => $"public partial record struct {name}{primary}",
        "generic-type" => $"public sealed partial class {name}<TC>{primary}",
        _ => throw new ArgumentException("shape.containing " + containing),
    };

    public static string ContainerSelf(string containing, string name) => containing == "generic-type" ? $"{name}<TC>" : name;
}
