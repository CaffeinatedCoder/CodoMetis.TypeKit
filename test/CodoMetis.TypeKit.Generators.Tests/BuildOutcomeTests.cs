using System.Diagnostics;
using System.Reflection;
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
    [InlineData("CMTK1005", "Outer<T>.Middle.DeeplyNested")]
    [InlineData("CMTK1005", "ArrayBacked")]
    [InlineData("CMTK1005", "NullableBacked")]
    [InlineData("CMTK1005", "DerivesFromAValueObject")]
    [InlineData("CMTK1005", "WrapsItself")]
    [InlineData("CMTK1005", "CycleOne")]
    [InlineData("CMTK1005", "CycleTwo")]
    [InlineData("CMTK1005", "StructCycleOne")]
    [InlineData("CMTK1005", "StructCycleTwo")]
    [InlineData("CMTK1005", "WrapsAValueObject")]
    [InlineData("CMTK1006", "NotSealed")]
    [InlineData("CMTK1007", "TakenName")]
    [InlineData("CMTK1007", "ShopId")]
    [InlineData("CMTK1007", "Shop.Id")]
    [InlineData("CMTK1008", "WithOperator")]
    [InlineData("CMTK1008", "WithObjectCompareTo")]
    [InlineData("CMTK1009", "CtorSameSignature")]
    [InlineData("CMTK1009", "CtorBypass")]
    [InlineData("CMTK1009", "CtorUnassignedStruct")]
    [InlineData("CMTK1009", "CtorUnassignedClass")]
    [InlineData("CMTK1009", "CtorParameterless")]
    [InlineData("CMTK1009", "CtorCopy")]
    [InlineData("CMTK1009", "PositionalStruct")]
    [InlineData("CMTK1009", "PositionalClass")]
    public void A_declaration_that_cannot_be_generated_is_an_error(string id, string type) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains($"'{type}"), $"{id} on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// Every value object over another one is refused, so a cycle would be refused without this
    /// reason too, but not told that it loops, or through which types.
    /// </summary>
    [Theory]
    [InlineData("WrapsItself", "it wraps itself (WrapsItself -> WrapsItself)")]
    [InlineData("CycleOne", "it wraps itself (CycleOne -> CycleTwo -> CycleOne)")]
    [InlineData("StructCycleTwo", "it wraps itself (StructCycleTwo -> StructCycleOne -> StructCycleTwo)")]
    [InlineData("WrapsAValueObject", "it wraps 'Fine', which is a value object itself; wrap 'int' instead")]
    public void A_value_object_over_a_value_object_is_refused_with_what_it_reaches(string type, string reason) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1005" && error.Message.StartsWith($"'{type}'") && error.Message.Contains(reason), $"CMTK1005 on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>
    /// A positional record is told that its parameter list is the constructor, since it declares
    /// none by that name.
    /// </summary>
    [Theory]
    [InlineData("CtorBypass", "the constructor CtorBypass.CtorBypass(string, int)")]
    [InlineData("PositionalStruct", "the parameter list (Guid Value)")]
    [InlineData("PositionalClass", "the parameter list (string Code)")]
    public void A_hand_written_constructor_is_refused_by_name(string type, string constructor) =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK1009" && error.Message.StartsWith($"'{type}' declares {constructor},"), $"CMTK1009 on {type}. The build reported:{Environment.NewLine}{consumer.Output}");

    /// <summary>A static constructor makes no instance, so the value object is still generated.</summary>
    [Fact]
    public void A_static_constructor_is_allowed() =>
        consumer.Errors.ShouldNotContain(error => error.Message.Contains("'WithStaticConstructor'"), consumer.Output);

    /// <summary>
    /// The declarations are refused with the errors above, never by an aspect that failed on them:
    /// an exception in an aspect or a compile error in the code it generated surfaces as LAMA0041 or
    /// LAMA0611, which tells the user nothing about their declaration.
    /// </summary>
    [Fact]
    public void Every_error_is_one_of_the_intended_ones() =>
        consumer.Errors.Select(error => error.Id).Distinct().Order()
                .ShouldBe(["CMTK0001", "CMTK0003", "CMTK0004", "CMTK0005", "CMTK0006", "CMTK0007", "CMTK1000", "CMTK1001", "CMTK1002", "CMTK1003", "CMTK1004", "CMTK1005", "CMTK1006", "CMTK1007", "CMTK1008", "CMTK1009"], ignoreOrder: false, customMessage: consumer.Output);

    /// <summary>
    /// The <c>GetValue</c>/<c>ValueOrNull</c> companions live in a namespace-level class. Named after
    /// the value object alone, <c>Order.Id</c> and <c>Customer.Id</c> both asked for <c>IdExtensions</c>,
    /// and Metalama crashed (LAMA0001) with the whole project unbuildable and no declaration named.
    /// </summary>
    [Fact]
    public void Nested_value_objects_of_one_name_get_a_companion_class_each() =>
        consumer.Errors.ShouldNotContain(error => error.Id.StartsWith("LAMA") || error.Message.Contains("'Order.Id'") || error.Message.Contains("'Customer.Id'"), consumer.Output);

    [Fact]
    public void The_analyzer_reports_a_default_value_object_beside_the_generators() =>
        consumer.Errors.ShouldContain(error => error.Id == "CMTK0001" && error.Message.Contains("'Fine'"), consumer.Output);

    /// <summary>
    /// The rules that read calls and members, in the project that declares the value objects. Metalama
    /// runs analyzers on the source before weaving, where a call to a generated member such as
    /// <c>TryFrom</c> or <c>FromKnownGood</c> does not bind: an operation-based rule missed both
    /// (measured 2026-09-28), and the verifier tests, which never weave, cannot see that. The consumer
    /// raises the warnings and the suggestion to errors so they are reported here.
    /// </summary>
    [Theory]
    [InlineData("CMTK0003", "The Option that 'TryFrom' returns")]
    [InlineData("CMTK0004", "Materialize rebuilds 'T'")]
    [InlineData("CMTK0005", "with a default 'Fine'")]
    [InlineData("CMTK0006", "'Unset' starts as a default 'Fine'")]
    [InlineData("CMTK0007", "'Target.FromKnownGood' is given 'input'")]
    public void A_rule_sees_the_value_objects_of_its_own_project(string id, string fragment) =>
        consumer.Errors.ShouldContain(error => error.Id == id && error.Message.Contains(fragment), consumer.Output);

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

            // Nested in a generic type through one that is not: T is still in scope, and the
            // generated code would have to name it where it cannot (an attribute's type argument,
            // a namespace-level companion class).
            public static class Outer<T> { public static class Middle { public readonly partial record struct DeeplyNested : IValue<int>; } }

            public readonly partial record struct ArrayBacked : IValue<int[]>;

            // Violates the notnull constraint, which is only a warning (CS8714). Without its own
            // refusal, the generated JSON converter and parsing fail to compile (LAMA0611/0612).
            public readonly partial record struct NullableBacked : IValue<int?>;

            // A record class that can be derived from: equality across a hierarchy is not value
            // equality, and a derived value object failed inside the generated code (LAMA0611).
            public partial record NotSealed : IValue<string>;

            public sealed partial record DerivesFromAValueObject : NotSealed;

            // A value object that reaches itself through what it wraps has no finite form: its JSON
            // converter serializes the wrapped value through the options, which is its own converter
            // again. As a record class it compiled without a word; as a struct it failed inside the
            // generated code (CS0523 as LAMA0611).
            public sealed partial record WrapsItself : IValue<WrapsItself>;

            public sealed partial record CycleOne : IValue<CycleTwo>;

            public sealed partial record CycleTwo : IValue<CycleOne>;

            public readonly partial record struct StructCycleOne : IValue<StructCycleTwo>;

            public readonly partial record struct StructCycleTwo : IValue<StructCycleOne>;

            // Over another value object, the surface depended on where that one was declared: over one
            // from the same project, parsing, comparison and the type converter were silently missing.
            public readonly partial record struct WrapsAValueObject : IValue<Fine>;

            // Companion classes are named after the whole nesting chain, so these two coexist.
            public sealed class Order { public readonly partial record struct Id : IValue<System.Guid>; }

            public sealed class Customer { public readonly partial record struct Id : IValue<System.Guid>; }

            // A companion class name that is taken, by a declared type or by another value object's
            // companion, is an error naming it, where it was an aspect exception or a crash.
            public readonly partial record struct TakenName : IValue<int>;

            public static class TakenNameExtensions { }

            public readonly partial record struct ShopId : IValue<int>;

            public static class Shop { public readonly partial record struct Id : IValue<int>; }

            // Comparison has one seam, a hand-written CompareTo(TSelf). A hand-written operator failed
            // the aspect (LAMA0500, naming no fix), and a hand-written object overload was kept
            // silently beside the generated generic one, which need not agree with it.
            public readonly partial record struct WithOperator : IValue<int>
            {
                public static bool operator <(WithOperator left, WithOperator right) => left.Value > right.Value;

                public static bool operator >(WithOperator left, WithOperator right) => left.Value < right.Value;
            }

            public readonly partial record struct WithObjectCompareTo : IValue<int>
            {
                public int CompareTo(object? obj) => 0;
            }

            // A value object's only constructor is the generated private one. A hand-written one with
            // its signature failed inside the generated code (LAMA0611), a positional record failed the
            // aspect (LAMA0041), and any other compiled: it skipped Create, or left Value null, and a
            // hand-written copy constructor did that to `with`. The bodies here touch nothing
            // generated, because a refused type is not generated.
            public readonly partial record struct CtorSameSignature : IValue<System.Guid>
            {
                public CtorSameSignature(System.Guid value) { }
            }

            public readonly partial record struct CtorBypass : IValidatedValue<CtorBypass, string, Fault>
            {
                public static Result<CtorBypass, Fault> Create(string value) => Result.Error(Fault.Refused);

                public CtorBypass(string value, int bypass) { }
            }

            public readonly partial record struct CtorUnassignedStruct : IValue<string>
            {
                public CtorUnassignedStruct(int unrelated) { }
            }

            public sealed partial record CtorUnassignedClass : IValue<string>
            {
                public CtorUnassignedClass(int unrelated) { }
            }

            public readonly partial record struct CtorParameterless : IValue<string>
            {
                public CtorParameterless() { }
            }

            public sealed partial record CtorCopy : IValue<string>
            {
                private CtorCopy(CtorCopy original) { }
            }

            public readonly partial record struct PositionalStruct(System.Guid Value) : IValue<System.Guid>;

            public sealed partial record PositionalClass(string Code) : IValue<string>;

            // A static constructor makes no instance: generated as usual.
            public readonly partial record struct WithStaticConstructor : IValue<int>
            {
                static WithStaticConstructor() { }
            }

            // A dictionary key is read by JsonMetadataServices.{TypeName}Converter, named after the
            // wrapped type. The probes cover int and decimal; a misnamed converter for any other number
            // would fail to compile here, as LAMA0611, rather than in a consumer's build.
            public readonly partial record struct AByte : IValue<byte>;
            public readonly partial record struct ASByte : IValue<sbyte>;
            public readonly partial record struct AShort : IValue<short>;
            public readonly partial record struct AUShort : IValue<ushort>;
            public readonly partial record struct AUInt : IValue<uint>;
            public readonly partial record struct ALong : IValue<long>;
            public readonly partial record struct AULong : IValue<ulong>;
            public readonly partial record struct AFloat : IValue<float>;
            public readonly partial record struct ADouble : IValue<double>;
            public readonly partial record struct AHalf : IValue<System.Half>;
            public readonly partial record struct AnInt128 : IValue<System.Int128>;
            public readonly partial record struct AUInt128 : IValue<System.UInt128>;

            public readonly partial record struct Fine : IValue<int>;

            public static class Uses
            {
                public static Fine Make() => default;

                public static WithStaticConstructor MakeWithStaticConstructor() => WithStaticConstructor.From(1);
            }

            public static class SameProject
            {
                public static void IgnoredTryFrom(int input) { Target.TryFrom(input); }

                public static Target KnownGoodFromInput(int input) => Target.FromKnownGood(input);

                public static Fine[] Slots(int count) => new Fine[count];

                public static T Materialized<T>(int value) where T : IValueObjectMaterializer<T, int> => T.Materialize(value);
            }

            public sealed class Holder
            {
                public Fine Unset { get; set; }
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

            // Only errors are read, so the rules that ship as warnings or a suggestion are raised here.
            await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"),
                """
                root = true

                [*.cs]
                dotnet_diagnostic.CMTK0003.severity = error
                dotnet_diagnostic.CMTK0005.severity = error
                dotnet_diagnostic.CMTK0006.severity = error
                dotnet_diagnostic.CMTK0007.severity = error
                """);

            // Stop MSBuild's upward search here, so the repository's own build settings (warnings as
            // errors, documentation, central package management) do not apply to the consumer.
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Build.targets"), "<Project />");
            await File.WriteAllTextAsync(Path.Combine(_directory, "Directory.Packages.props"),
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");

            // --no-dependencies and RestoreRecursive=false: the referenced projects are already built
            // by this test run, and restoring them again could rewrite restore output that other
            // tests read concurrently. Already built in this run's configuration, which is therefore
            // passed on: without it the consumer builds in Debug and, in a Release run such as CI's,
            // finds no referenced assembly at all.
            using var process = Process.Start(new ProcessStartInfo(
                "dotnet", ["build", _directory, "-c", Configuration, "--no-dependencies", "-p:RestoreRecursive=false", "-nodeReuse:false", "-clp:NoSummary"])
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

        /// <summary>The configuration this test assembly, and so the projects it references, was built in.</summary>
        private static string Configuration =>
            typeof(Consumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
         ?? throw new InvalidOperationException("The test assembly carries no AssemblyConfigurationAttribute.");

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
