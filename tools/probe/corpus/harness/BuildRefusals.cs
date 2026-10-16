// The declarations the generators refuse (shape.refused-kind), written as C# per row, and the consumer build that shows what the
// generators did with them (C22). The build is BuildConsumer.cs's (a throwaway consumer under artifacts/, against the projects this run
// already built, outside the repository's build settings) with Metalama's transformed files kept, since a refused declaration fails the
// build and leaves no assembly to read: the transformed source is where a member the generators added to a refused type would show.
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Probes.Build;

/// <summary>A consumer build: its exit code, its output, its diagnostics, and Metalama's transformed source of each file.</summary>
public sealed record RefusalOutcome(int ExitCode, string Output, IReadOnlyList<BuildDiagnostic> Diagnostics, IReadOnlyDictionary<string, string> Transformed)
{
    /// <summary>The diagnostics reported in one of the consumer's files.</summary>
    public IEnumerable<BuildDiagnostic> In(string file) => Diagnostics.Where(d => Path.GetFileName(d.File) == file);
}

/// <summary>
/// One refusal of the Generators README's "Build errors" table, as a row declares it: the id the table gives it, the obligation of C22
/// that states it, the consumer it is built in, and the compiler errors that may stand beside it in its own file.
/// </summary>
/// <remarks>
/// A compiler error in a consumer hid every LAMA0611 of the other files (e4b751c), so the declarations whose refusal comes with a
/// compiler error of its own are built apart: a refused closed set leaves its interface's members unimplemented (CS0535, as
/// BuildOutcomeTests allows it), and a hand-written equality operator of a record is also C#'s CS0111 (the README says so).
/// </remarks>
public sealed record Refusal(string Kind, string Id, string Obligation, string Group, IReadOnlyList<string> Companions);

/// <summary>One row's declarations: the refused type, as the diagnostics name it, and a control the generators must generate.</summary>
public sealed record RefusedDeclaration(int Index, Refusal Refusal, string Name, string Display, string Control, string File, string Source);

public static partial class Refusals
{
    /// <summary>The refusals C22 declares, by their shape.refused-kind value; any other value is the harness's mistake, never a pass.</summary>
    public static readonly IReadOnlyDictionary<string, Refusal> Table = new Refusal[]
    {
        new("unsealed-record-class (CMTK1006)", "CMTK1006", "unsealed-record-class", "value-objects", []),
        new("over-value-object (CMTK1005)", "CMTK1005", "over-value-object", "value-objects", []),
        new("generic-wrapped (CMTK1005)", "CMTK1005", "generic-wrapped", "value-objects", []),
        // The label keeps the id it had when C09's and C11's rows were generated; an abstract value object is CMTK1022 since 1ef30be.
        new("abstract (CMTK1005)", "CMTK1022", "abstract", "value-objects", []),
        new("over-object (CMTK1005)", "CMTK1005", "over-object", "value-objects", []),
        new("over-System.Enum (CMTK1005)", "CMTK1005", "over-system-enum", "value-objects", []),
        new("named-as-generated-member (CMTK1005)", "CMTK1005", "named-as-generated-member", "value-objects", []),
        new("closed-set-named-as-generated-member (CMTK1021)", "CMTK1021", "closed-set-named-as-generated-member", "closed-sets", ["CS0535"]),
        new("equality-operator (CMTK1011)", "CMTK1011", "equality-operator", "operators", ["CS0111"]),
    }.ToDictionary(r => r.Kind);

    /// <summary>The value-object names CMTK1005 refuses (the README's list), and the closed-set names CMTK1021 refuses; a row takes one by its index.</summary>
    static readonly string[] ValueObjectNames = ["Value", "From", "TryFrom", "FromKnownGood", "Revalidate", "Parse", "TryParse", "TryFormat", "MinValue", "MaxValue", "CompareTo"];
    static readonly string[] ClosedSetNames = ["Name", "QualifiedName", "All", "Match", "Parse", "TryParse"];

    /// <summary>The link every generator error ends with (Generators README, Build errors: "Every error ends with a link to this table").</summary>
    public const string TableLink = "https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit.Generators/README.md#build-errors";

    static readonly string[] Structs = ["record-struct", "validated-struct"];

    /// <summary>
    /// The row's declarations. Reads shape.refused-kind, shape.kind, shape.site, shape.inheritance and build-host, and shape.wrapped where
    /// the refused kind wraps it. A refused kind the table does not hold, a combination the claim excludes, and a refusal the README's
    /// Build errors table no longer lists are harness errors: the row would show nothing about a refusal the README states.
    /// </summary>
    public static RefusedDeclaration Declare(Probes.Shapes.Row r)
    {
        if (r["build-host"] != "sdk-build") throw new InvalidOperationException("build-host " + r["build-host"] + " has no build here");
        var kindValue = r["shape.refused-kind"];
        if (!Table.TryGetValue(kindValue, out var refusal)) throw new InvalidOperationException($"shape.refused-kind '{kindValue}' is no refusal this harness declares");
        if (!ReadmeLists(refusal.Id)) throw new InvalidOperationException($"the Generators README's Build errors table has no row for {refusal.Id}");
        var kind = r["shape.kind"]; var site = r["shape.site"]; var inheritance = r["shape.inheritance"];
        var n = r.Index; var id = n.ToString();
        var closedSet = kind == "closed-set";
        if (closedSet != (refusal.Group == "closed-sets")) throw new InvalidOperationException($"{kindValue} cannot be declared as a {kind} (excluded in claims.json)");
        if (Structs.Contains(kind) && refusal.Obligation is "unsealed-record-class" or "abstract") throw new InvalidOperationException($"a {kind} cannot be {refusal.Obligation} (excluded in claims.json)");
        if ((Structs.Contains(kind) || closedSet) && inheritance is not ("none" or "explicit-interface-member")) throw new InvalidOperationException($"a {kind} has no base record for {inheritance} (excluded in claims.json)");
        if (site == "in-generic-type") throw new InvalidOperationException("in-generic-type is a refusal of its own (excluded in claims.json)");

        var name = refusal.Obligation switch
        {
            "named-as-generated-member" => ValueObjectNames[n % ValueObjectNames.Length],
            "closed-set-named-as-generated-member" => ClosedSetNames[n % ClosedSetNames.Length],
            _ => $"V{id}",
        };
        var control = closedSet ? $"S{id}" : $"K{id}";
        var body = new System.Text.StringBuilder();
        body.AppendLine($"public enum VF{id} {{ No }}");
        var explicitMember = inheritance == "explicit-interface-member";
        if (explicitMember) body.AppendLine($"public interface IMark{id} {{ int Mark(); }}");

        // The bases, from the topmost down: plain abstract records, the direct one with an explicit IEquatable of its own or an explicit
        // member of another interface where shape.inheritance says so. A struct or a closed set implements that interface itself.
        var depth = inheritance switch { "base-depth-2" => 2, "base-depth-3" => 3, "none" => 0, _ => 1 };
        if (closedSet || Structs.Contains(kind)) depth = 0;
        for (var d = depth; d >= 1; d--)
        {
            var b = $"B{id}_{d}";
            var parents = new List<string>();
            if (d < depth) parents.Add($"B{id}_{d + 1}");
            var members = "";
            if (d == 1 && inheritance == "base-explicit-IEquatable") { parents.Add($"IEquatable<{b}>"); members = $"bool IEquatable<{b}>.Equals({b}? other) => ReferenceEquals(this, other);"; }
            if (d == 1 && explicitMember) { parents.Add($"IMark{id}"); members = $"int IMark{id}.Mark() => 0;"; }
            body.AppendLine($"public abstract record {b}{(parents.Count > 0 ? " : " + string.Join(", ", parents) : "")} {{ {members} }}");
        }
        var baseList = depth > 0 ? $"B{id}_1, " : "";
        var ownMark = explicitMember && depth == 0;
        var markList = ownMark ? $", IMark{id}" : "";
        var markMember = ownMark ? $" int IMark{id}.Mark() => 0;" : "";

        if (closedSet)
        {
            body.AppendLine($"public sealed partial class {name} : IClosedSet<{name}>{markList} {{ public static readonly {name} A{id} = new();{markMember} }}");
            body.AppendLine($"public sealed partial class {control} : IClosedSet<{control}>{markList} {{ public static readonly {control} A{id} = new();{markMember} }}");
        }
        else
        {
            var w = refusal.Obligation is "over-object" or "over-system-enum" ? null : Src.Wrapped(r["shape.wrapped"], id);
            var wrapped = refusal.Obligation switch
            {
                "over-value-object" => $"I{id}",
                "generic-wrapped" => $"List<{w}>",
                "over-object" => "object",
                "over-system-enum" => "System.Enum",
                _ => w!,
            };
            if (refusal.Obligation == "over-value-object") body.AppendLine($"public readonly partial record struct I{id} : IValue<{w}>;");
            var modifier = refusal.Obligation switch { "unsealed-record-class" => "", "abstract" => "abstract ", _ => "sealed " };
            var operators = refusal.Obligation == "equality-operator";
            body.AppendLine(ValueObject(kind, name, modifier, baseList, wrapped, id, markList, markMember, operators));
            body.AppendLine(ValueObject(kind, control, "sealed ", baseList, w ?? "int", id, markList, markMember, operators: false));
        }

        var source = Src.Usings + $"namespace C22R{id};\n" + Src.WrappedDeclarations(id) + Src.AtSite(site, id, body.ToString()) + "\n";
        return new(n, refusal, name, Nesting(site, id) + name, control, $"R{id}.cs", source);
    }

    static string ValueObject(string kind, string name, string modifier, string baseList, string wrapped, string id, string markList, string markMember, bool operators)
    {
        var validated = kind.StartsWith("validated", StringComparison.Ordinal);
        var marker = validated ? $"IValidatedValue<{name}, {wrapped}, VF{id}>" : $"IValue<{wrapped}>";
        var head = Structs.Contains(kind)
            ? $"public readonly partial record struct {name} : {marker}{markList}"
            : $"public {modifier}partial record {name} : {baseList}{marker}{markList}";
        var members = new List<string>();
        if (validated) members.Add($"public static Result<{name}, VF{id}> Create({wrapped} value) => Result.Error(VF{id}.No);");
        if (operators)
        {
            var t = Structs.Contains(kind) ? name : name + "?";
            members.Add($"public static bool operator ==({t} left, {t} right) => true;");
            members.Add($"public static bool operator !=({t} left, {t} right) => false;");
        }
        if (markMember.Length > 0) members.Add(markMember.Trim());
        return members.Count == 0 ? head + ";" : $"{head}\n{{\n    {string.Join("\n    ", members)}\n}}";
    }

    /// <summary>shape.site's nesting as a diagnostic names a type: its containing types, dotted.</summary>
    static string Nesting(string site, string id) => site switch
    {
        "top-level" => "", "nested-in-class" or "nested-in-struct" => $"O{id}.", "nested-two-levels" => $"O{id}.P{id}.",
        _ => throw new ArgumentException("shape.site " + site),
    };

    static readonly Lazy<string> Readme = new(() => File.ReadAllText(Path.Combine(Consumer.RepositoryRoot(), "src", "CodoMetis.TypeKit.Generators", "README.md")));

    /// <summary>Whether the README's Build errors table has a row for <paramref name="id"/>.</summary>
    public static bool ReadmeLists(string id) =>
        Readme.Value.IndexOf("\n## Build errors", StringComparison.Ordinal) is var at and >= 0
        && Readme.Value.IndexOf($"\n| {id} |", at, StringComparison.Ordinal) >= 0;

    /// <summary>Whether the README still has the heading the table link points at.</summary>
    public static bool ReadmeHasBuildErrorsHeading() => Readme.Value.Split('\n').Any(l => l.TrimEnd('\r') == "## Build errors");

    /// <summary>
    /// How the generators left a type in Metalama's transformed source: <c>null</c> where its declaration is as it was written (no base
    /// type and no member added, and no <c>{Name}Extensions</c> companion), else what they added. Throws where the type is not there.
    /// </summary>
    public static string? Added(RefusalOutcome build, RefusedDeclaration d, string name)
    {
        if (!build.Transformed.TryGetValue(d.File, out var transformed)) throw new InvalidOperationException($"Metalama wrote no transformed {d.File}");
        var written = One(CSharpSyntaxTree.ParseText(d.Source).GetRoot(), name, "the row's source");
        var woven = One(CSharpSyntaxTree.ParseText(transformed).GetRoot(), name, "the transformed " + d.File);
        var added = new List<string>();
        var bw = written.BaseList?.Types.Count ?? 0; var bt = woven.BaseList?.Types.Count ?? 0;
        if (bt != bw) added.Add($"{bt - bw} base type(s)");
        if (woven.Members.Count != written.Members.Count) added.Add($"{woven.Members.Count - written.Members.Count} member(s)");
        var companion = name + "Extensions";
        foreach (var (file, text) in build.Transformed)
            if (text.Contains($"namespace C22R{d.Index}", StringComparison.Ordinal) && text.Contains(companion, StringComparison.Ordinal)
                && CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == companion))
                added.Add($"the companion {companion} ({file})");
        return added.Count == 0 ? null : string.Join(", ", added);
    }

    static TypeDeclarationSyntax One(Microsoft.CodeAnalysis.SyntaxNode root, string name, string where)
    {
        var found = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => t.Identifier.Text == name).ToList();
        return found.Count == 1 ? found[0] : throw new InvalidOperationException($"{found.Count} declarations of {name} in {where}");
    }

    static readonly Dictionary<string, Lazy<Task<RefusalOutcome>>> Builds = new();

    /// <summary>Builds the consumer of <paramref name="files"/> once per <paramref name="key"/> in this process.</summary>
    public static Task<RefusalOutcome> Build(string key, Func<IReadOnlyDictionary<string, string>> files)
    {
        lock (Builds)
        {
            if (!Builds.TryGetValue(key, out var build)) Builds[key] = build = new(() => Run(files()));
            return build.Value;
        }
    }

    static string Configuration => typeof(Refusals).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                                   ?? throw new InvalidOperationException("the probe assembly carries no AssemblyConfigurationAttribute");

    static async Task<RefusalOutcome> Run(IReadOnlyDictionary<string, string> files)
    {
        var root = Consumer.RepositoryRoot();
        var dir = Path.Combine(root, "artifacts", $"probe-refusals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(root, "src");
            await File.WriteAllTextAsync(Path.Combine(dir, "Consumer.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <AssemblyName>ProbeRefusals</AssemblyName>
                    <MetalamaEmitCompilerTransformedFiles>true</MetalamaEmitCompilerTransformedFiles>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{src}/CodoMetis.TypeKit.Generators/CodoMetis.TypeKit.Generators.csproj" />
                    <Reference Include="NodaTime"><HintPath>{typeof(NodaTime.Instant).Assembly.Location}</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(dir, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
            foreach (var (name, text) in files) await File.WriteAllTextAsync(Path.Combine(dir, name), text);

            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
            var info = new ProcessStartInfo(dotnet, ["build", dir, "-c", Configuration, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary", "-v:q", "-nologo"])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir,
            };
            // The run's code coverage instruments this process through its environment; the build is not the probe's code.
            foreach (var k in info.Environment.Keys.ToList())
                if (k.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
                    || k.Contains("CODECOVERAGE", StringComparison.OrdinalIgnoreCase) || k.StartsWith("MicrosoftInstrumentationEngine", StringComparison.OrdinalIgnoreCase)
                    || k.Equals("DOTNET_STARTUP_HOOKS", StringComparison.OrdinalIgnoreCase))
                    info.Environment.Remove(k);
            info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10))) await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            var diagnostics = Line().Matches(output)
                .Select(m => new BuildDiagnostic(m.Groups["file"].Value.Trim(), m.Groups["sev"].Value, m.Groups["id"].Value, m.Groups["msg"].Value.Trim()))
                .Distinct().ToList();
            var woven = Path.Combine(dir, "obj", Configuration, "net10.0", "metalama");
            var transformed = Directory.Exists(woven)
                ? Directory.GetFiles(woven, "*.cs").ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f))
                : new Dictionary<string, string>();
            return new RefusalOutcome(process.ExitCode, output, diagnostics, transformed);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [GeneratedRegex(@"^(?<file>[^\r\n(]+)\((?<line>\d+),(?<col>\d+)\): (?<sev>warning|error) (?<id>[A-Z]+\d+): (?<msg>.*?)(?: \[[^\]\r\n]*\])?\r?$", RegexOptions.Multiline)]
    private static partial Regex Line();
}
