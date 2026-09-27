using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Generators.Tests;

/// <summary>
/// What a consumer's build reports for declarations the generators cannot generate, and that the
/// analyzer works beside the real generators assembly.
/// </summary>
/// <remarks>
/// <para>
/// These outcomes are build errors, so no test in a compiling project can observe them. The fixture
/// builds a throwaway consumer that references the generators and the analyzer as a package consumer
/// would get them, and every test reads its diagnostics.
/// </para>
/// <para>
/// CMTK0002 looks for the generators by assembly identity. Its unit tests use a stand-in assembly;
/// this is where the real one has to keep it silent.
/// </para>
/// </remarks>
public sealed partial class BuildOutcomeTests(BuildOutcomeTests.Consumer consumer) : IClassFixture<BuildOutcomeTests.Consumer>
{
    [Theory]
    [InlineData("CMTK1000", "NotPartial")]
    [InlineData("CMTK1001", "NotRecord")]
    [InlineData("CMTK1002", "NotReadonly")]
    [InlineData("CMTK1003", "TwoMarkers")]
    [InlineData("CMTK1004", "Misnamed")]
    [InlineData("CMTK1005", "Generic")]
    [InlineData("CMTK1005", "ArrayBacked")]
    [InlineData("CMTK1005", "NullableBacked")]
    public void A_declaration_that_cannot_be_generated_is_an_error(string id, string type) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains($"'{type}"), $"{id} on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// The declarations are refused with the errors above, never by an aspect that failed on them:
    /// an exception in an aspect or a compile error in the code it generated surfaces as LAMA0041 or
    /// LAMA0611, which tells the user nothing about their declaration.
    /// </summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones() =>
        consumer.Errors.Select(error => error.Id).Distinct().Order()
                .ShouldBe(["CMTK0001", "CMTK1000", "CMTK1001", "CMTK1002", "CMTK1003", "CMTK1004", "CMTK1005"], ignoreOrder: false, customMessage: consumer.Output);

    [Fact]
    public void The_analyzer_reports_a_default_value_object_beside_the_generators() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK0001" && error.Message.Contains("'Fine'"), consumer.Output);

    [Fact]
    public void CMTK0002_stays_silent_when_the_real_generators_assembly_is_referenced() =>
        consumer.Errors.ShouldNotContain(error => error.Id == "CMTK0002", consumer.Output);

    public sealed record Diagnostic(string Id, string Message);

    /// <summary>The consumer, built once per test run.</summary>
    public sealed partial class Consumer : IAsyncLifetime
    {
        private const string Declarations =
            """
            using CodoMetis.TypeKit;
            using CodoMetis.TypeKit.ValueObjects;

            namespace Consumer;

            public readonly record struct NotPartial : IValue<int>;

            public readonly partial struct NotRecord : IValue<int>;

            public partial record struct NotReadonly : IValue<int>;

            public readonly partial record struct TwoMarkers : IValue<int>, IValue<string>;

            public enum Fault { Refused }

            public readonly partial record struct Target : IValidatedValue<Target, int, Fault>
            {
                public static Result<Target, Fault> Create(int value) => Result.Error(Fault.Refused);
            }

            public readonly partial record struct Misnamed : IValidatedValue<Target, int, Fault>
            {
                public static Result<Target, Fault> Create(int value) => Target.Create(value);
            }

            public readonly partial record struct Generic<T> : IValue<int>;

            public readonly partial record struct ArrayBacked : IValue<int[]>;

            // Violates the notnull constraint, which is only a warning (CS8714). Without its own
            // refusal, the generated JSON converter and parsing fail to compile (LAMA0611/0612).
            public readonly partial record struct NullableBacked : IValue<int?>;

            public readonly partial record struct Fine : IValue<int>;

            public static class Uses
            {
                public static Fine Make() => default;
            }
            """;

        /// <summary>
        /// Under the ignored <c>artifacts/</c> rather than the temp folder: on macOS that is behind the
        /// <c>/var</c> symlink, and NuGet's relative project paths then resolve against the wrong side.
        /// </summary>
        private readonly string _directory = Path.Combine(RepositoryRoot(), "artifacts", $"consumer-{Guid.NewGuid():N}");

        public IReadOnlyList<Diagnostic> Errors { get; private set; } = [];

        public string Output { get; private set; } = "";

        public async ValueTask InitializeAsync()
        {
            Directory.CreateDirectory(_directory);

            var src = Path.Combine(RepositoryRoot(), "src");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Consumer.csproj"),
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <Nullable>enable</Nullable>
                     <ImplicitUsings>enable</ImplicitUsings>
                   </PropertyGroup>
                   <ItemGroup>
                     <ProjectReference Include="{src}/CodoMetis.TypeKit.Generators/CodoMetis.TypeKit.Generators.csproj" />
                     <ProjectReference Include="{src}/CodoMetis.TypeKit.Analyzers/CodoMetis.TypeKit.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                   </ItemGroup>
                 </Project>
                 """);
            await File.WriteAllTextAsync(Path.Combine(_directory, "Declarations.cs"), Declarations);

            // Stop MSBuild's upward search here, so the repository's own build settings (warnings as
            // errors, documentation, central package management) do not apply to the consumer.
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            // --no-dependencies and RestoreRecursive=false: the referenced projects are already built
            // by this test run, and restoring them again could rewrite restore output that other
            // tests read concurrently.
            using var process = Process.Start(new ProcessStartInfo(
                "dotnet", ["build", _directory, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                Environment            = { ["DOTNET_CLI_UI_LANGUAGE"] = "en" },
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Output = await output + await errors;
            Errors = [.. DiagnosticLine().Matches(Output).Select(match => new Diagnostic(match.Groups["id"].Value, match.Groups["message"].Value)).Distinct()];

            Errors.ShouldNotBeEmpty($"The consumer build reported no errors at all, so it did not run as intended:{Environment.NewLine}{Output}");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static string RepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx"))) return directory.FullName;
            }

            throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
        }

        [GeneratedRegex(@"error (?<id>[A-Z]+\d+): (?<message>.*?)(?: \[|$)", RegexOptions.Multiline)]
        private static partial Regex DiagnosticLine();
    }
}
