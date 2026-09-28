using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// The analyzers reach consumers because <c>CodoMetis.TypeKit</c> depends on the analyzer package,
/// and the analyzer package ships its assembly under <c>analyzers/dotnet/cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both halves fail silently. Measured 2026-09-27: a project reference with
/// <c>ReferenceOutputAssembly="false"</c> emits no dependency at all, and the build stays green
/// while every consumer loses CMTK0001 and CMTK0002. So these tests read what NuGet actually
/// emits, from a real pack, rather than the project files.
/// </para>
/// <para>
/// The dependency must not exclude <c>Analyzers</c> either. On SDK 10.0.401 an excluded analyzer
/// still loads, because the SDK takes analyzers from every package in the restore graph, but the
/// dependency should say what is intended rather than lean on that.
/// </para>
/// </remarks>
[Collection(PacksCollection.Name)]
public sealed class AnalyzerPackagingTests(AnalyzerPackagingTests.Packs packs)
{
    private const string BasePackage     = "CodoMetis.TypeKit";
    private const string AnalyzerPackage = "CodoMetis.TypeKit.Analyzers";

    [Fact]
    public void The_base_package_depends_on_the_analyzer_package_for_every_framework()
    {
        var groups = Named(packs.Nuspec(BasePackage), "group").ToList();

        groups.ShouldNotBeEmpty("the base package declares no dependency groups");

        foreach (var group in groups)
        {
            var dependency = group.Elements().Where(element => element.Name.LocalName == "dependency")
                                  .SingleOrDefault(element => (string?)element.Attribute("id") == AnalyzerPackage);

            dependency.ShouldNotBeNull(
                $"{BasePackage} ({group.Attribute("targetFramework")?.Value}) has no dependency on {AnalyzerPackage}, so no consumer gets the analyzers.");

            var excluded = ((string?)dependency.Attribute("exclude") ?? "").Split(',', StringSplitOptions.TrimEntries);
            excluded.ShouldNotContain("Analyzers", StringComparer.OrdinalIgnoreCase,
                $"The dependency on {AnalyzerPackage} excludes its analyzers. Set PrivateAssets=\"none\" on the reference.");
        }
    }

    [Fact]
    public void The_analyzer_package_ships_the_analyzer_and_nothing_to_compile_against()
    {
        var entries = packs.Entries(AnalyzerPackage);

        entries.ShouldContain($"analyzers/dotnet/cs/{AnalyzerPackage}.dll");
        entries.Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).ShouldBeEmpty(
            "The analyzer package has a lib/ folder, so every consumer would compile against the analyzer assembly.");
    }

    [Fact]
    public void The_analyzer_package_is_not_a_development_dependency()
    {
        var flag = Named(packs.Nuspec(AnalyzerPackage), "developmentDependency").SingleOrDefault()?.Value;

        (flag ?? "false").ShouldBe("false", "A development dependency does not flow to the consumers of CodoMetis.TypeKit.");
    }

    /// <summary>
    /// The analyzer compiles against the oldest Roslyn a .NET 10 SDK runs, 5.0.0 (the 10.0.1xx band,
    /// still serviced, and the only band Linux distributions build). Measured 2026-09-28: built
    /// against 5.9.0 and run by the 5.0.0 compiler, the analyzer is not loaded (CS9057, a warning),
    /// and <c>default(Option&lt;int&gt;)</c> compiles without CMTK0001.
    /// </summary>
    [Fact]
    public void The_packed_analyzer_loads_in_the_oldest_NET_10_compiler()
    {
        var floor      = new Version(5, 0, 0, 0);
        var references = packs.AssemblyReferences(AnalyzerPackage, $"analyzers/dotnet/cs/{AnalyzerPackage}.dll");

        references.Keys.ShouldContain("Microsoft.CodeAnalysis");

        foreach (var (name, version) in references.Where(reference => reference.Key.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)))
        {
            version.ShouldBeLessThanOrEqualTo(floor,
                $"The analyzer references {name} {version}. A compiler older than that (SDK 10.0.1xx runs Roslyn {floor}) " +
                "refuses to load it with CS9057, and every CMTK rule goes silent. Keep Microsoft.CodeAnalysis.CSharp at the floor in Directory.Packages.props.");
        }
    }

    /// <summary>
    /// Where Metalama compiles a project, an analyzer it is not told about sees only the source,
    /// before the Razor compiler's output exists. Measured 2026-09-28: every rule went silent in a
    /// component of a project that references a value-object project. The package names the rules in
    /// its buildTransitive props, and this holds every analyzer in the packed assembly to that list,
    /// so a rule declared in another namespace is not left out.
    /// </summary>
    [Fact]
    public void Every_analyzer_runs_on_the_code_Metalama_transformed()
    {
        var props  = XDocument.Parse(packs.Text(AnalyzerPackage, $"buildTransitive/{AnalyzerPackage}.props"));
        var listed = Named(props, "MetalamaTransformedCodeAnalyzer").Select(item => (string?)item.Attribute("Include")).ToHashSet(StringComparer.Ordinal);

        var analyzers = packs.DiagnosticAnalyzers(AnalyzerPackage, $"analyzers/dotnet/cs/{AnalyzerPackage}.dll");

        analyzers.ShouldNotBeEmpty("The packed analyzer assembly declares no [DiagnosticAnalyzer] type, so this test checks nothing.");
        analyzers.Where(analyzer => !listed.Contains(analyzer) && !listed.Contains(analyzer[..analyzer.LastIndexOf('.')]))
                 .ShouldBeEmpty(
                     "These analyzers are not in buildTransitive/CodoMetis.TypeKit.Analyzers.props, so in a project Metalama compiles they " +
                     "never see a .razor or .cshtml file. Add their namespace as a MetalamaTransformedCodeAnalyzer item.");
    }

    /// <summary>
    /// Elements by local name. NuGet picks the nuspec's XML namespace by the features a package
    /// uses (a development dependency is written against the 2010/07 schema, a plain package against
    /// 2013/05), so a query for one namespace finds nothing in the other and passes vacuously.
    /// </summary>
    private static IEnumerable<XElement> Named(XDocument nuspec, string localName) =>
        nuspec.Descendants().Where(element => element.Name.LocalName == localName);

    /// <summary>
    /// Every shipping package, packed once per test run from the current sources and shared by the
    /// packaging test classes through <see cref="PacksCollection"/>: two fixtures packing the same
    /// project at once would race on its build output.
    /// </summary>
    public sealed class Packs : IAsyncLifetime
    {
        private readonly string _output = Path.Combine(Path.GetTempPath(), $"codometis-typekit-pack-{Guid.NewGuid():N}");

        public async ValueTask InitializeAsync()
        {
            foreach (var project in Repository.ShippingProjects())
                await Pack(project);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_output)) Directory.Delete(_output, recursive: true);
            return ValueTask.CompletedTask;
        }

        public XDocument Nuspec(string package)
        {
            using var archive = ZipFile.OpenRead(Package(package));
            using var stream  = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();

            return XDocument.Load(stream);
        }

        public IReadOnlyList<string> Entries(string package)
        {
            using var archive = ZipFile.OpenRead(Package(package));

            return [.. archive.Entries.Select(entry => entry.FullName)];
        }

        /// <summary>
        /// The <see cref="System.Reflection.AssemblyMetadataAttribute"/> pairs of the package's
        /// <c>lib/net10.0/{package}.dll</c>, or <see langword="null"/> when it packs no such assembly.
        /// Read from the metadata, never loaded.
        /// </summary>
        public IReadOnlyDictionary<string, string?>? AssemblyMetadata(string package)
        {
            using var archive = ZipFile.OpenRead(Package(package));
            var entry = archive.GetEntry($"lib/net10.0/{package}.dll");
            if (entry is null) return null;

            using var image = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(image);
            image.Position = 0;

            using var pe = new PEReader(image);
            var metadata = pe.GetMetadataReader();

            return metadata.GetAssemblyDefinition().GetCustomAttributes()
                           .Select(metadata.GetCustomAttribute)
                           .Where(attribute => IsAssemblyMetadata(metadata, attribute))
                           .Select(attribute => attribute.DecodeValue(StringArguments.Instance))
                           .ToDictionary(value => (string)value.FixedArguments[0].Value!, value => (string?)value.FixedArguments[1].Value, StringComparer.Ordinal);
        }

        /// <summary>The assemblies an assembly in the package references, by name, read from its metadata.</summary>
        public IReadOnlyDictionary<string, Version> AssemblyReferences(string package, string path)
        {
            using var pe = Assembly(package, path);
            var metadata = pe.GetMetadataReader();

            return metadata.AssemblyReferences
                           .Select(metadata.GetAssemblyReference)
                           .ToDictionary(reference => metadata.GetString(reference.Name), reference => reference.Version, StringComparer.Ordinal);
        }

        /// <summary>A text file in the package, read whole.</summary>
        public string Text(string package, string path)
        {
            using var archive = ZipFile.OpenRead(Package(package));
            var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"{package} packs no {path}.");

            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        /// <summary>
        /// The full names of the types marked <c>[DiagnosticAnalyzer]</c> in an assembly of the package,
        /// read from its metadata, never loaded.
        /// </summary>
        public IReadOnlyList<string> DiagnosticAnalyzers(string package, string path)
        {
            using var pe = Assembly(package, path);
            var metadata = pe.GetMetadataReader();

            return [.. metadata.TypeDefinitions
                               .Select(metadata.GetTypeDefinition)
                               .Where(type => type.GetCustomAttributes().Select(metadata.GetCustomAttribute)
                                                  .Any(attribute => IsAttribute(metadata, attribute, typeof(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzerAttribute))))
                               .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")];
        }

        /// <summary>
        /// The types an assembly in the package declares with
        /// <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/>, each with the tool the
        /// attribute names: what a source generator baked into it. Read from the metadata, never loaded.
        /// </summary>
        public IReadOnlyList<(string Type, string Tool)> GeneratedTypes(string package, string path)
        {
            using var pe = Assembly(package, path);
            var metadata = pe.GetMetadataReader();

            return
            [
                .. from type in metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
                   from attribute in type.GetCustomAttributes().Select(metadata.GetCustomAttribute)
                   where IsAttribute(metadata, attribute, typeof(System.CodeDom.Compiler.GeneratedCodeAttribute))
                   select (Type: $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}".TrimStart('.'),
                           Tool: (string?)attribute.DecodeValue(StringArguments.Instance).FixedArguments[0].Value ?? "")
            ];
        }

        private PEReader Assembly(string package, string path)
        {
            using var archive = ZipFile.OpenRead(Package(package));
            var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"{package} packs no {path}.");

            var image = new MemoryStream();
            using (var stream = entry.Open()) stream.CopyTo(image);
            image.Position = 0;

            return new PEReader(image);
        }

        private static bool IsAssemblyMetadata(MetadataReader metadata, CustomAttribute attribute) =>
            IsAttribute(metadata, attribute, typeof(System.Reflection.AssemblyMetadataAttribute));

        private static bool IsAttribute(MetadataReader metadata, CustomAttribute attribute, Type type) =>
            attribute.Constructor.Kind == HandleKind.MemberReference
         && metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
         && metadata.GetTypeReference((TypeReferenceHandle)parent) is var reference
         && metadata.GetString(reference.Name) == type.Name
         && metadata.GetString(reference.Namespace) == type.Namespace;

        /// <summary>Enough of a type provider to decode an attribute whose arguments are strings.</summary>
        private sealed class StringArguments : ICustomAttributeTypeProvider<Type>
        {
            public static readonly StringArguments Instance = new();

            public Type GetPrimitiveType(PrimitiveTypeCode typeCode) =>
                typeCode == PrimitiveTypeCode.String ? typeof(string) : throw new NotSupportedException(typeCode.ToString());

            public Type GetSystemType() => typeof(Type);

            public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();

            public Type GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => throw new NotSupportedException();

            public Type GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => throw new NotSupportedException();

            public Type GetTypeFromSerializedName(string name) => throw new NotSupportedException();

            public PrimitiveTypeCode GetUnderlyingEnumType(Type type) => throw new NotSupportedException();

            public bool IsSystemType(Type type) => type == typeof(Type);
        }

        private string Package(string package) =>
            Directory.GetFiles(_output, $"{package}.*.nupkg")
                     .Where(path => !path.EndsWith(".snupkg", StringComparison.Ordinal))
                     .Single(path => char.IsDigit(Path.GetFileName(path)[package.Length + 1]));

        /// <summary>
        /// <c>--no-restore</c>: the convention tests read restore output, and a restore here could
        /// rewrite it while another test reads it.
        /// </summary>
        private async Task Pack(string project)
        {
            var csproj = Path.Combine(Repository.Root, "src", project, $"{project}.csproj");

            using var process = Process.Start(new ProcessStartInfo("dotnet", ["pack", csproj, "--no-restore", "-c", "Release", "-o", _output, "-nodeReuse:false"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            process.ExitCode.ShouldBe(0, $"dotnet pack {project} failed:{Environment.NewLine}{await output}{await errors}");
        }
    }
}

/// <summary>The one <see cref="AnalyzerPackagingTests.Packs"/> the packaging test classes share.</summary>
[CollectionDefinition(Name)]
public sealed class PacksCollection : ICollectionFixture<AnalyzerPackagingTests.Packs>
{
    public const string Name = "Packs";
}
