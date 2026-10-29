// End-to-end check of the path a consumer of the *packages* takes, which nothing else in the suite
// covers.
//
// Every other layer references the projects directly, and a ProjectReference hides exactly what
// breaks for a package consumer: a nuspec that forgets a dependency, an analyzer that never reaches
// the consumer's compiler, a fabric that weaves in this repository and not in a project that gets
// the generators transitively, a satellite that works through a project reference and not through
// the assembly a consumer restores. Each of those packs cleanly and restores cleanly.
//
// So this packs the packages, restores them into throwaway projects created *outside* the
// repository (inside it, Directory.Build.props would apply and they would stop resembling anything a
// consumer builds), compiles real code against them, runs it, and asserts on what it prints, on the
// diagnostics the build reports, and on the resolved package graph. Never on the exit code alone.
//
// Five consumers, their sources under Consumers/:
//   core     references only CodoMetis.TypeKit: Option and Result work, no Metalama arrives, and
//            CMTK0001 and CMTK0002 fire (design/analyzers.md).
//   layered  a domain library references CodoMetis.TypeKit.Generators, and an app reaches everything
//            only through that library: the app uses the generated members, the transitive fabric
//            generates a value object the app declares itself, and CMTK0001 fires in the app, in the
//            domain library, and in a Razor component of a third project; CMTK0011 and CMTK0013 in
//            the domain library, on code Metalama wove.
//   host     a web host with both satellites: EF Core maps the value objects and translates .Value,
//            and the OpenAPI document describes them.
//   aot      the same ground published with Native AOT (design/native-aot.md): no trim or AOT warning
//            from a package or from the code woven into the consumer, and the native binary does
//            what the JIT build does.
//   walkthrough  the orders service docs/walkthrough.md follows, on all four packages a web service uses: every
//            answer, log line and SQL statement the page states in a comment is what it gives, read from the page
//            (WalkthroughPage.cs).
//
// Native AOT needs the platform linker: clang (and zlib on Linux), which the workflows install, or
// MSVC's C++ build tools on Windows.
//
// Usage: dotnet tools/ConsumerSmokeTest/bin/Release/net10.0/consumer-smoke-test.dll [feed-directory] [--only <consumer>]
//   With no argument the packages are packed fresh. Pass a directory of existing .nupkg files (the
//   release workflow passes its pack output) to test exactly the artifacts being shipped. --only runs one
//   consumer, for a local check: CI runs them all.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

Console.Out.NewLine = "\n";
var root = SourceFingerprint.FindRoot();
if (SourceFingerprint.Difference(root) is { } stale)
{
    Console.Error.WriteLine($"consumer-smoke-test: this dll was built from other sources than the checkout holds ({stale}). Run `{SourceFingerprint.RebuildCommand}`.");
    return SourceFingerprint.StaleExitCode;
}
var at = Array.IndexOf(args, "--only");
var only = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
string[] positional = at >= 0 ? [.. args[..at], .. args[Math.Min(at + 2, args.Length)..]] : args;
if (positional.Length > 1 || (at >= 0 && only is not ("core" or "layered" or "host" or "aot" or "walkthrough")))
{
    Console.Error.WriteLine("Usage: consumer-smoke-test.dll [feed-directory] [--only core|layered|host|aot|walkthrough]");
    return 2;
}
return new ConsumerSmokeTest.Smoke(root!, positional.FirstOrDefault(), only).Run();

namespace ConsumerSmokeTest
{
    /// <summary>A command that failed where it had to succeed, or the reverse: the run stops, as the script's <c>set -e</c> did.</summary>
    internal sealed class Stop : Exception;

    internal sealed partial class Smoke(string repoRoot, string? givenFeed, string? only)
    {
        private readonly DirectoryInfo _created = Directory.CreateTempSubdirectory("consumer-smoke-test-");
        private string Work => _created.FullName;
        private string _cwd = "";
        private string _dotnet = "dotnet";
        private string _version = "";
        private bool _failed;

        public int Run()
        {
            try
            {
                Main();
                if (_failed)
                {
                    Console.WriteLine();
                    Console.WriteLine("consumer smoke test FAILED");
                    return 1;
                }
                Console.WriteLine();
                Console.WriteLine($"consumer smoke test passed ({_version})");
                return 0;
            }
            catch (Stop)
            {
                return 1;
            }
            finally
            {
                try { _created.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private void Main()
        {
            // A private package cache, and not an optimisation to skip.
            //
            // NuGet resolves id+version from the global packages folder before it looks at any source, so with
            // the version held at the next release a locally built package is shadowed by whatever that version
            // was restored as before, including one published on nuget.org. The test then reports on a package
            // that has nothing to do with the working tree. A sibling repository's smoke test passed a
            // deliberately sabotaged build that way.
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(Work, "packages"));
            _cwd = repoRoot;

            // Every dotnet below, the consumers' too, is the one that satisfies global.json: the installer's when
            // the one first on PATH cannot (Homebrew's on macOS), as the verdict tool does (design/adr/0105). Started
            // by its full path: .NET looks for a bare `dotnet` beside the running host before it reads PATH.
            var (dotnet, dotnetDirectory) = Dotnet.For(repoRoot);
            _dotnet = dotnet;
            if (dotnetDirectory is not null)
            {
                Environment.SetEnvironmentVariable("PATH", dotnetDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
                Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetDirectory);
                Console.WriteLine($"note: the dotnet on PATH cannot satisfy global.json; using {dotnet}");
            }

            _version = Run("dotnet", "msbuild", Path.Combine(repoRoot, "src", "CodoMetis.TypeKit", "CodoMetis.TypeKit.csproj"), "-getProperty:Version").Trim();

            // Every shipping project is a package, discovered from src/ as the release workflow does, so a new
            // package is held to "it is in the feed" without an edit here.
            var packages = Directory.GetDirectories(Path.Combine(repoRoot, "src"))
                                    .Select(Path.GetFileName).OfType<string>()
                                    .Where(id => File.Exists(Path.Combine(repoRoot, "src", id, $"{id}.csproj")))
                                    .Order(StringComparer.Ordinal)
                                    .ToList();

            string feed;
            if (string.IsNullOrEmpty(givenFeed))
            {
                feed = Path.Combine(Work, "feed");
                Console.WriteLine($"==> Packing {_version}");
                Run("dotnet", "build", Path.Combine(repoRoot, "CodoMetis.TypeKit.slnx"), "-c", "Release", "--verbosity", "quiet");
                Run("dotnet", "pack", Path.Combine(repoRoot, "CodoMetis.TypeKit.slnx"), "-c", "Release", "--no-build", "-o", feed, "--verbosity", "quiet");
            }
            else
            {
                feed = Path.GetFullPath(givenFeed);
                if (!Directory.Exists(feed))
                {
                    Console.WriteLine($"FAILED: {givenFeed} is not a directory");
                    throw new Stop();
                }
                Console.WriteLine($"==> Using existing feed: {feed}");
            }

            foreach (var id in packages)
            {
                if (!File.Exists(Path.Combine(feed, $"{id}.{_version}.nupkg")))
                {
                    Console.WriteLine($"FAILED: {id}.{_version}.nupkg is not in {feed}");
                    throw new Stop();
                }
            }
            Console.WriteLine($"    {packages.Count} packages: {string.Join(' ', packages)}");

            // One configuration for every consumer, at the top of the work directory, where NuGet and the SDK
            // find it from each project below.
            //
            // Source mapping, not just source ordering: this repository's ids must come from the local feed and
            // nowhere else, because a version on nuget.org satisfying the restore would mean testing something
            // unrelated to the working tree. Everything else (Metalama, EF Core, ASP.NET) has to come from
            // nuget.org, so restricting the whole restore to the local feed is not an option either: that fails
            // with NU1101 on the transitive dependencies. <clear/> drops any machine-level sources.
            //
            // The repository's global.json comes along, so the consumers build on the SDK band the packages were
            // built and tested with, also on a CI runner that carries several SDKs.
            File.WriteAllText(Path.Combine(Work, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{System.Security.SecurityElement.Escape(feed)}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="local">
                      <package pattern="CodoMetis.TypeKit*" />
                    </packageSource>
                    <packageSource key="nuget.org">
                      <package pattern="*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>

                """);
            File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(Work, "global.json"));

            // host builds on layered's Domain project, so it brings layered along.
            if (only is null or "core") Core();
            if (only is null or "layered" or "host") Layered();
            if (only is null or "host") Host();
            if (only is null or "aot") Aot();
            if (only is null or "walkthrough") Walkthrough();
        }

        // ── core: the base package alone ───────────────────────────────────────────────────────────────

        private void Core()
        {
            Console.WriteLine("==> core: only CodoMetis.TypeKit, and its test assertions");
            NewProject("console", "core", "CodoMetis.TypeKit", "CodoMetis.TypeKit.Testing");
            Write("Program.cs", "core/Program.cs");

            Run("dotnet", "build");
            ListPackages("core.packages");
            Save("core.out", Run("dotnet", "run", "--no-build", "--no-launch-profile"));
            Print("core.out");

            Console.WriteLine("==> core: asserting");
            AssertContains("> CodoMetis.TypeKit ", "core.packages", "the base package resolved from the feed");
            AssertContains("> CodoMetis.TypeKit.Analyzers ", "core.packages", "the analyzer package arrived with it");
            AssertContains("> CodoMetis.TypeKit.Testing ", "core.packages", "the test assertions resolved from the feed");
            AssertNotContains("Metalama", "core.packages", "no Metalama in the graph of a base-only consumer");
            AssertVersions("core.packages");
            AssertContains("some=Some 3", "core.out", "Option.Some matches its value");
            AssertContains("none=None", "core.out", "Option.None matches none");
            AssertNotContains("424242", "core.out", "an Option's ToString never prints its content");
            AssertContains("ok=Ok 5", "core.out", "a bare value converts to a successful Result");
            AssertContains("refused=Error nope", "core.out", "Result.Error converts to a failed Result");
            AssertContains("uninitialized=throws", "core.out", "a default Result throws rather than picking a branch");
            AssertContains("cancelled=Ok/Error gone", "core.out", "Result<TError> converts from the success marker and a bare error");
            AssertContains("asserted=3/nope", "core.out", "the assertions hand back the value and the error");
            AssertContains("assertion-failed=Expected an error, but the Result<Int32, String> is a success (its value is not printed).", "core.out", "a failed assertion says what the outcome was");

            // The analyzers run in the consumer's compiler. Three rules at once: a default Option, a value
            // object declared in a project that has no generator to implement it, and a fault of one type twice,
            // whose [Distinct] parameters the analyzer reads from the package's metadata; and one breach of each
            // kind a type parameter can demand of its argument, declared in the consumer itself.
            Write("Guard.cs", "core/Guard.cs");
            Save("core.guard", RunFailing("dotnet", "build", "--no-incremental"));
            AssertContains("CMTK0001", "core.guard", "CMTK0001 fires for default(Option<int>) in a base-only consumer");
            AssertContains("CMTK0002", "core.guard", "CMTK0002 fires for a value object with no generator");
            AssertContains("error CMTK0010: 'DayOfWeek', the type argument for 'T2' of 'Fault<T1, T2>', is the same type as the argument for 'T1'", "core.guard", "CMTK0010 fires for a fault of one type twice, from the attribute in the package's metadata");
            AssertContains("error CMTK0010: 'string', the type argument for 'T' of 'Proxy<T>', is not an interface", "core.guard", "CMTK0010 fires for [Interface]");
            AssertContains("error CMTK0010: 'string', the type argument for 'T' of 'Derivable<T>', is not a class that can be derived from", "core.guard", "CMTK0010 fires for [Unsealed]");
            AssertContains("error CMTK0010: 'Guid', the type argument for 'T' of 'Keyed<T>', is not a value object (IValue<T>)", "core.guard", "CMTK0010 fires for [ValueObject]");
            AssertContains("error CMTK0010: 'DayOfWeek', the type argument for 'T' of 'Named<T>', is not a closed set (IClosedSet<TSelf>)", "core.guard", "CMTK0010 fires for [ClosedSet]");
            AssertContains("error CMTK0010: 'DayOfWeek', the type argument for 'T' of 'Flagged<T>', is not an enum marked [Flags]", "core.guard", "CMTK0010 fires for [FlagsEnum]");
            AssertContains("error CMTK0010: 'Cursor', the type argument for 'T' of 'Pinned<T>', is not a readonly struct", "core.guard", "CMTK0010 fires for [ReadOnlyStruct]");
            AssertContains("error CMTK0010: 'Cursor', the type argument for 'TRight' of 'Apart<TLeft, TRight>', derives from, implements, boxes to or is a base of 'object', the type argument for 'TLeft'", "core.guard", "CMTK0010 fires for [Unrelated]");
            Delete("Guard.cs");
        }

        // ── layered: the generators in a domain library, an app that reaches them through it ───────────

        private void Layered()
        {
            Console.WriteLine("==> layered: Domain references CodoMetis.TypeKit.Generators");
            NewProject("classlib", "layered/Domain", "CodoMetis.TypeKit.Generators");
            Write("Shop.cs", "layered/Domain/Shop.cs");

            Console.WriteLine("==> layered: App references only Domain, and logs with Serilog");
            NewProject("console", "layered/App", "CodoMetis.TypeKit.Serilog");
            Run("dotnet", "add", "reference", "../Domain/Domain.csproj");
            Write("Program.cs", "layered/App/Program.cs");

            Run("dotnet", "build");
            ListPackages("layered.packages");
            Save("layered.out", Run("dotnet", "run", "--no-build", "--no-launch-profile"));
            Print("layered.out");

            Console.WriteLine("==> layered: asserting");
            AssertContains("> CodoMetis.TypeKit.Generators ", "layered.packages", "the generators reach the app through Domain");
            AssertContains("> CodoMetis.TypeKit.Analyzers ", "layered.packages", "so do the analyzers");
            AssertVersions("layered.packages");
            AssertContains("guid-version=7", "layered.out", "OrderId.New() is a version 7 Guid");
            AssertContains("""json={"Code":"ABC","Quantity":3}""", "layered.out", "value objects serialize as the value they wrap");
            AssertContains("tryfrom=refused", "layered.out", "TryFrom applies Create");
            AssertContains("json-read=refused, names the rule: True", "layered.out", "the generated JSON converter applies Create");
            AssertContains("parsed=42", "layered.out", "the generated Parse");
            AssertContains("local=X-1", "layered.out", "the transitive fabric generates a value object the app declares");
            AssertContains("> CodoMetis.TypeKit.Serilog ", "layered.packages", "the Serilog satellite resolved from the feed");
            AssertContains("""serilog=Held { Secret: "***", Quantity: "3" }""", "layered.out", "Serilog logs value objects as what they print");
            AssertNotContains("hunter2", "layered.out", "a masked value object is never logged through its Value");

            Write("Guard.cs", "layered/App/Guard.cs");
            Save("layered.guard", RunFailing("dotnet", "build", "--no-incremental"));
            AssertContains("CMTK0001", "layered.guard", "CMTK0001 fires in a project that reaches the package only through another");
            AssertNotContains("CMTK0002", "layered.guard", "no CMTK0002 where the generators arrive transitively");
            Delete("Guard.cs");

            // Metalama compiles every project that reaches the generators, and runs source generators after its
            // transformation, so an analyzer it is not told about sees neither a component's generated C# nor
            // the members the aspects add. The analyzer package's buildTransitive props tells it. Without them,
            // both builds below succeed: measured 2026-09-28.
            Console.WriteLine("==> layered: the rules run on the code Metalama transformed");
            _cwd = Path.Combine(Work, "layered", "Domain");
            Write("Guard.cs", "layered/Domain/Guard.cs");
            Save("layered.declaring", Lines(RunFailing("dotnet", "build", "--no-incremental"), "Guard.cs("));
            AssertContains("error CMTK0001", "layered.declaring", "CMTK0001 fires in the declaring project for a local typed by a generated member");
            AssertContains("error CMTK0013: 'Swapped.Open' is written outside its declaration", "layered.declaring", "CMTK0013 fires in the declaring project for a member assigned in a woven set's static constructor");
            AssertContains("error CMTK0011", "layered.declaring", "CMTK0011 fires for a type without the constructor its interface requires");
            Delete("Guard.cs");

            // By hand: the razorclasslib template takes no --framework.
            _cwd = Path.Combine(Work, "layered", "Components");
            Directory.CreateDirectory(_cwd);
            Write("Components.csproj", "layered/Components/Components.csproj");
            Write("Probe.razor", "layered/Components/Probe.razor");
            Save("layered.razor", Lines(RunFailing("dotnet", "build", "--no-incremental"), "Probe.razor("));
            AssertContains("error CMTK0001", "layered.razor", "CMTK0001 fires in a Razor component of a project that reaches the generators");
        }

        // ── host: both satellites, from packages ───────────────────────────────────────────────────────

        private void Host()
        {
            Console.WriteLine("==> host: EF Core and OpenAPI satellites");
            NewProject("web", "host", "CodoMetis.TypeKit.EntityFrameworkCore", "CodoMetis.TypeKit.AspNetCore");
            // Third-party packages at the versions the satellites are built against, read from central
            // management. Microsoft.AspNetCore.OpenApi is referenced directly, as the package README says: it
            // enables its source generator's interceptors in build/, which NuGet imports for a direct reference
            // only, and a host that gets it only through the satellite fails with CS9137.
            Run("dotnet", "add", "package", "Microsoft.EntityFrameworkCore.Sqlite", "--version", CentralVersion("Microsoft.EntityFrameworkCore.Relational"));
            Run("dotnet", "add", "package", "Microsoft.AspNetCore.OpenApi", "--version", CentralVersion("Microsoft.AspNetCore.OpenApi"));
            Run("dotnet", "add", "reference", "../layered/Domain/Domain.csproj");
            Write("Program.cs", "host/Program.cs");

            Run("dotnet", "build");
            ListPackages("host.packages");
            Save("host.out", Run("dotnet", "run", "--no-build", "--no-launch-profile"));
            Print("host.out");

            Console.WriteLine("==> host: asserting");
            AssertContains("> CodoMetis.TypeKit.EntityFrameworkCore ", "host.packages", "the EF Core satellite resolved from the feed");
            AssertContains("> CodoMetis.TypeKit.AspNetCore ", "host.packages", "the OpenAPI satellite resolved from the feed");
            AssertVersions("host.packages");
            AssertContains("""openapi-component={"type":"string","format":"uuid"}""", "host.out", "OrderId is a uuid component in the document");
            AssertContains("""openapi-parameter={"type":"string","format":"uuid"}""", "host.out", "the route parameter is a uuid");
            AssertContains("""openapi-items={"$ref":"#/components/schemas/Quantity"}""", "host.out", "a list of value objects keeps its items");
            AssertContains("bound=200", "host.out", "a route parameter binds through the generated TryParse");
            AssertContains("malformed=400", "host.out", "a malformed route parameter is a 400");
            AssertContains("\"o\".\"Code\" LIKE", "host.out", ".Value in a query translates to the bare column");
            AssertContains("found=ABC:2", "host.out", "value objects round-trip through the database");
        }

        // ── aot: the packages under Native AOT ─────────────────────────────────────────────────────────
        //
        // The packages build with the trim and AOT analyzers on, but those cannot see the code the
        // generators weave into a consumer, and they cannot tell that a run-time path still works. Under
        // Native AOT an interface nothing uses is removed from a type, and reflection over it finds nothing:
        // that is how the OpenAPI satellite once described every value object as {} with no warning at any
        // build (design/native-aot.md). So this publishes a consumer with PublishAot, reads the publish warnings,
        // and runs the native binary.
        //
        // The value objects live in namespace Shop, so a warning about woven code names Shop.; a warning in
        // a package names CodoMetis. TrimmerSingleWarn=false keeps the packages' warnings itemised instead of
        // folded into one line per assembly. EF Core's own warnings (its DbContext constructor requires
        // dynamic code) name neither and are EF's to fix.

        private void Aot()
        {
            Console.WriteLine("==> aot: the packages published with Native AOT");
            if (!OperatingSystem.IsWindows() && OnPath("clang") is null)
            {
                Console.WriteLine("FAILED: Native AOT needs clang on PATH (and zlib on Linux)");
                throw new Stop();
            }

            NewProject("classlib", "aot/Domain", "CodoMetis.TypeKit.Generators");
            Run("dotnet", "add", "package", "NodaTime", "--version", CentralVersion("NodaTime"));
            Write("Shop.cs", "aot/Domain/Shop.cs");

            // Two applications: JSON and OpenAPI in a web app, EF Core in a console app of its own, the way its
            // compiled model is generated.
            //
            // What a run cannot see: anything that uses IValueObject<,> (the EF satellite's converter, or the
            // generated [GeneratedValueObject<,>] through its constraint) makes the trimmer keep that interface
            // on every value object, so a GetInterfaces() lookup passes here as well (measured). Before the
            // attribute existed it found nothing, and the OpenAPI assertions below failed. That lookup is kept
            // out by the analyzers, whose IL2070 fails the packages' build, and by AGENTS.md.
            File.WriteAllText(Path.Combine(Work, "aot.warnings"), "");
            File.WriteAllText(Path.Combine(Work, "aot.out"), "");

            // JSON and OpenAPI.
            AotProject("web", "aot/Web", "CodoMetis.TypeKit.AspNetCore");
            Run("dotnet", "add", "package", "Microsoft.AspNetCore.OpenApi", "--version", CentralVersion("Microsoft.AspNetCore.OpenApi"));
            Write("Program.cs", "aot/Web/Program.cs");
            ListPackages("aot.packages");
            AotPublishAndRun("Web");

            // EF Core: a compiled model and precompiled queries, generated before publishing, which is how EF
            // runs under Native AOT at all. The id query maps its value-object parameter at run time.
            AotProject("console", "aot/Data", "CodoMetis.TypeKit.EntityFrameworkCore");
            var ef = CentralVersion("Microsoft.EntityFrameworkCore.Relational");
            Run("dotnet", "add", "package", "Microsoft.EntityFrameworkCore.Sqlite", "--version", ef);
            Run("dotnet", "add", "package", "Microsoft.EntityFrameworkCore.Design", "--version", ef);
            Write("Program.cs", "aot/Data/Program.cs");
            Run("dotnet", "new", "tool-manifest");
            Run("dotnet", "tool", "install", "dotnet-ef", "--version", ef);
            Run("dotnet", "ef", "dbcontext", "optimize", "--precompile-queries", "--nativeaot");
            ListPackages("aot.data.packages");
            AotPublishAndRun("Data");
            Print("aot.out");

            Console.WriteLine("==> aot: asserting");
            AssertVersions("aot.packages");
            AssertVersions("aot.data.packages");
            var warnings = Read("aot.warnings");
            if (warnings.Length > 0)
            {
                Console.WriteLine("  FAIL: trim or AOT warnings from the packages or the woven code");
                foreach (var line in SplitLines(warnings).Take(20)) Console.WriteLine("      " + line);
                _failed = true;
            }
            else
            {
                Console.WriteLine("  ok: no trim or AOT warning names a package or a woven value object");
            }
            AssertContains("""json={"Id":"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee","Code":"ABC","Name":"n","Quantity":3,"Amount":1.5,"Link":"https://x.test/a","Weekday":5,"ByQuantity":{"7":1},"ByDay":{"Monday":2}}""",
                           "aot.out", "every JSON strategy writes through a context that lists no wrapped type");
            AssertContains("json-roundtrip=True", "aot.out", "and reads back what it wrote");
            AssertContains("json-refused=THREW JsonException: Code refused the JSON value (NotUpperCase)", "aot.out", "the generated JSON converter applies Create");
            AssertContains("json-number-handling=\"2.5\"", "aot.out", "the options' number handling applies to a wrapped number");
            AssertContains("stored=abc", "aot.out", "StoredJsonConverterFactory reads without the rules");
            AssertContains("""decimal-string="7.8" True string""", "aot.out", "a decimal converter reaches a value object through a context that lists no decimal, and describes it");
            AssertContains("""decimal-factory="7.8" string {"Price":"7.8"} string string""", "aot.out", "a factory in the options and one a property names are asked for the converter they make, and the stored-JSON factory keeps the generated description");
            AssertContains("openapi-decimal=\"string\" \"decimal\"", "aot.out", "the document describes a value object over a decimal as the described converter writes it");
            AssertContains("option-refused=THREW NotSupportedException: Option<Int32> is not a wire type", "aot.out", "Option refuses JSON with its own message");
            AssertContains("parse=42 Monday https://p.test/ 2026-09-27", "aot.out", "the generated Parse, including through a TypeConverter");
            AssertContains("parse-after-lookup=True 2026-09-27T12:00:00Z", "aot.out", "and after TypeDescriptor described the wrapped type unregistered");
            AssertContains("tryparse-refused=False", "aot.out", "the generated TryParse applies Create");
            AssertContains("openapi=200", "aot.out", "the OpenAPI document is generated");
            AssertContains("""openapi-component={"type":"string","format":"uuid"}""", "aot.out", "OrderId is a uuid component");
            AssertContains("""openapi-parameter={"type":"string","format":"uuid"}""", "aot.out", "the route parameter is a uuid");
            AssertContains("""openapi-items={"$ref":"#/components/schemas/Quantity"}""", "aot.out", "a list of value objects keeps its items");
            AssertContains("""openapi-set={"enum":["Open","Shipped"],"type":"string"}""", "aot.out", "a closed set is a string enum of its members' names");
            AssertContains("bound=200", "aot.out", "a route parameter binds through the generated TryParse");
            AssertContains("result-success=\"ABC\"", "aot.out", "ToHttpResult answers a success with its value");
            AssertContains("result-fault=422 application/problem+json NotUpperCase", "aot.out", "and a fault as problem details with its mapped status");
            AssertContains("result-responses=200,400,422", "aot.out", "the document lists the value and each mapped status");
            AssertContains("fault-kind=404 NotFound The account does not exist. ACC-NotFound", "aot.out", "a fault set is answered by its kind, its description the detail, the host's extension from the fault feature");
            AssertContains("fault-strict=422 EmailTaken - ACC-EmailTaken", "aot.out", "an endpoint's status replaces the kind's");
            AssertContains("fault-detail=409 EmailTaken In use since 3 May. ACC-EmailTaken", "aot.out", "a Fault's detail reaches the problem");
            AssertContains("fault-rule=409 Billing.Rule.Reserved", "aot.out", "a nested enum's rule is named after its nesting chain under Native AOT");
            AssertContains("fault-rule-set=409 OrderStatus.Shipped", "aot.out", "a closed set's rule is its qualified name");
            AssertContains("fault-created=201 /accounts/a1", "aot.out", "the endpoint's own success, with its Location");
            AssertContains("fault-responses=200,404,422", "aot.out", "the document lists the endpoint's status in place of the kind's");
            AssertContains("composed-shared=422 NotUpperCase - ACC-NotUpperCase", "aot.out", "a fault of the second type is answered as its type is mapped");
            AssertContains("composed-own=409 EmailTaken In use since 3 May. ACC-EmailTaken", "aot.out", "and one of the first, with its detail");
            AssertContains("composed-responses=204,400,409,422", "aot.out", "the document lists both types' statuses, ascending, without the member the route leaves out");
            AssertContains("malformed=400", "aot.out", "a malformed route parameter is a 400");
            AssertContains("patch=Value(ABC) Null", "aot.out", "a Patch<T> reads a value object and a null through the context under Native AOT");
            AssertContains("""set-json={"Status":"Shipped","Counts":{"Open":1}}""", "aot.out", "a closed set writes its name through the context, as a value and as a dictionary key");
            AssertContains("set-json-read=True 2", "aot.out", "and reads the members themselves back");
            AssertContains("patch-refused=400", "aot.out", "a value object a patch wraps still refuses");
            AssertContains("""openapi-patch={"type":["null","string"],"description":"A JSON Merge Patch field: absent leaves the value as it is, null clears it, a value sets it."} required=none""", "aot.out", "a patch property is its wrapped type's schema, inlined and nullable, and not required");
            AssertContains("ef-insert=1", "aot.out", "EF Core writes value objects through the compiled model");
            AssertContains("ef-value=2", "aot.out", ".Value translates in a precompiled query");
            AssertContains("ef-parameter=ABC", "aot.out", "a value-object parameter is mapped at run time");
            AssertContains("ef-stored=lower", "aot.out", "a stored value the rules refuse is read without them");
            AssertContains("ef-identity=1,2", "aot.out", "a key over an integer is generated by the database");
            AssertContains("""ef-set-stored=Shipped {"Quantity":3,"Status":"Open"}""", "aot.out", "a closed set is its name in a column and in a JSON column, through the compiled model");
            AssertContains("ef-set-parameter=Shipped", "aot.out", "a closed-set parameter is mapped at run time");
            AssertContains("ef-json-column=Open 3", "aot.out", "a JSON column reads a closed set and a value object back in a precompiled query");
            AssertContains("ef-first-or-none=THREW InvalidOperationException: Query wasn't precompiled", "aot.out", "a query helper is not precompiled, as the docs say: the base package's FirstOrNone on a query");
            AssertContains("ef-first-or-none-async=THREW InvalidOperationException: Query wasn't precompiled", "aot.out", "and the EF satellite's FirstOrNoneAsync");
            AssertContains("ef-first-or-none-written=True", "aot.out", "the docs' workaround, the operators written at the call site, runs");
        }

        private void AotProject(string template, string path, params string[] packages)
        {
            NewProject(template, path, packages);
            Run("dotnet", "add", "reference", "../Domain/Domain.csproj");
            // PublishAot, the interceptors EF's precompiled queries are, and itemised package warnings.
            var project = Path.Combine(_cwd, $"{Path.GetFileName(path)}.csproj");
            File.WriteAllText(project, File.ReadAllText(project).Replace("</PropertyGroup>",
                "  <PublishAot>true</PublishAot>\n    <TrimmerSingleWarn>false</TrimmerSingleWarn>\n" +
                "    <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.EntityFrameworkCore.GeneratedInterceptors</InterceptorsNamespaces>\n  </PropertyGroup>",
                StringComparison.Ordinal));
        }

        // Publishes, keeps the warnings that name a package or a woven value object, runs the native binary.
        private void AotPublishAndRun(string name)
        {
            var output = Path.Combine(Work, "aot", $"{name}.out");
            var publish = Run("dotnet", "publish", "-c", "Release", "-o", output);
            Save($"aot.{name}.publish", publish);
            var warnings = SplitLines(publish).Where(line => IlWarning().IsMatch(line) && PackageOrWoven().IsMatch(line))
                                             .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            File.AppendAllLines(Path.Combine(Work, "aot.warnings"), warnings);

            var native = Path.Combine(output, OperatingSystem.IsWindows() ? $"{name}.exe" : name);
            var executable = File.Exists(native) && (OperatingSystem.IsWindows() || File.GetUnixFileMode(native).HasFlag(UnixFileMode.UserExecute));
            if (executable && !File.Exists(Path.Combine(output, $"{name}.dll")))
            {
                Console.WriteLine($"  ok: {name} is published as native code");
            }
            else
            {
                Console.WriteLine($"  FAIL: {name} was not published as a native executable");
                _failed = true;
            }
            File.AppendAllText(Path.Combine(Work, "aot.out"), Run(native) + "\n");
        }

        // ── walkthrough: the orders service of docs/walkthrough.md ─────────────────────────────────────
        //
        // One web project on the four packages a service uses, as a reader would set it up: what the page shows is
        // what it compiles. It prints each answer as name=value, the SQL EF Core runs and its log lines, and every
        // expectation is read from the page's comments (WalkthroughPage), never written here: a changed answer fails
        // until the page says it. ReadmeSampleTests holds the page's blocks to these sources, so an edit to a stated
        // answer is an edit here too, which selects the pack lane; WalkthroughTests runs the same service in memory
        // on every test leg.

        private void Walkthrough()
        {
            Console.WriteLine("==> walkthrough: the orders service docs/walkthrough.md follows");
            NewProject("web", "walkthrough", "CodoMetis.TypeKit.Generators", "CodoMetis.TypeKit.EntityFrameworkCore", "CodoMetis.TypeKit.AspNetCore", "CodoMetis.TypeKit.Serilog");
            Run("dotnet", "add", "package", "Microsoft.EntityFrameworkCore.Sqlite", "--version", CentralVersion("Microsoft.EntityFrameworkCore.Relational"));
            Run("dotnet", "add", "package", "Microsoft.AspNetCore.OpenApi", "--version", CentralVersion("Microsoft.AspNetCore.OpenApi"));
            Run("dotnet", "add", "package", "Serilog.Sinks.Console", "--version", CentralVersion("Serilog.Sinks.Console"));
            foreach (var source in (string[])["Orders.cs", "OrderEndpoints.cs", "Tour.cs", "Program.cs"]) Write(source, $"walkthrough/{source}");

            Run("dotnet", "build");
            ListPackages("walkthrough.packages");
            Save("walkthrough.out", Run("dotnet", "run", "--no-build", "--no-launch-profile"));
            Print("walkthrough.out");

            Console.WriteLine("==> walkthrough: asserting against docs/walkthrough.md");
            AssertContains("> CodoMetis.TypeKit.Generators ", "walkthrough.packages", "the generators resolved from the feed");
            AssertContains("> CodoMetis.TypeKit.Serilog ", "walkthrough.packages", "the Serilog package resolved from the feed");
            AssertVersions("walkthrough.packages");

            var page = File.ReadAllText(Path.Combine(repoRoot, WalkthroughPage.Path));
            var lines = SplitLines(Read("walkthrough.out")).ToList();
            var answers = lines.Select(line => AnswerLine().Match(line)).Where(match => match.Success)
                               .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);

            foreach (var name in WalkthroughPage.Declarations(page).Union(answers.Keys, StringComparer.Ordinal))
            {
                var value = answers.GetValueOrDefault(name);
                AssertStated(page, WalkthroughPage.Answer(name), stated => value is not null && WalkthroughPage.Is(stated, value),
                             value is null ? $"{name} is stated, and the service never answered it" : $"{name} is what the page states");
            }
            var logged = lines.Where(line => line.StartsWith("[INF] ", StringComparison.Ordinal)).ToList();
            AssertStated(page, "log.Information(\"Placed", stated => WalkthroughPage.Within(stated, logged), "the log line is what the page states: the address masked");
            AssertStated(page, "order.Quantity.Value >= minimum.Value", stated => WalkthroughPage.Within(stated, lines), ".Value in the query is the column in the SQL EF Core logs");
            AssertStated(page, "Database.EnsureCreated()", stated => WalkthroughPage.Within(stated, lines), "each value object is a column of its underlying type");
            if (logged.Count == 0 || logged.Any(line => line.Contains("ann@example.com", StringComparison.Ordinal)))
            {
                Console.WriteLine("  FAIL: the e-mail address is masked in every log line, and there is one");
                foreach (var line in logged) Console.WriteLine("      " + line);
                _failed = true;
            }
            else
            {
                Console.WriteLine("  ok: the e-mail address is masked in every log line");
            }
        }

        // What the page states for the fragment holds; a page that no longer states it fails too.
        private void AssertStated(string page, string fragment, Func<IReadOnlyList<string>, bool> holds, string message)
        {
            IReadOnlyList<string> stated;
            try
            {
                stated = WalkthroughPage.Stated(page, fragment);
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine($"  FAIL: {message} ({e.Message})");
                _failed = true;
                return;
            }
            if (holds(stated))
            {
                Console.WriteLine($"  ok: {message}");
                return;
            }
            Console.WriteLine($"  FAIL: {message} (the page states '{string.Join(" / ", stated)}' for '{fragment.Trim()}')");
            _failed = true;
        }

        [GeneratedRegex(@"^(?<name>[a-z]\w*)=(?<value>.*)$")]
        private static partial Regex AnswerLine();

        // ── the steps ──────────────────────────────────────────────────────────────────────────────────

        // Creates a project from a template under the work directory and installs packages into it. No
        // --source flag on `dotnet add package`: it would override the mapping in nuget.config and restrict
        // the whole restore, transitive dependencies included, to the local feed.
        private void NewProject(string template, string path, params string[] packages)
        {
            _cwd = Path.Combine(Work, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(_cwd);
            Run("dotnet", "new", template, "--framework", "net10.0", "--output", ".");
            Delete("Class1.cs");
            foreach (var package in packages) Run("dotnet", "add", "package", package, "--version", _version);
        }

        private void ListPackages(string file) => Save(file, Run("dotnet", "list", "package", "--include-transitive"));

        // Third-party packages at the versions the satellites are built against, from central management.
        private string CentralVersion(string id)
        {
            var match = Regex.Match(File.ReadAllText(Path.Combine(repoRoot, "Directory.Packages.props")),
                                    $@"Include=""{Regex.Escape(id)}"" Version=""(?<version>[^""]*)""");
            if (match.Success) return match.Groups["version"].Value;
            Console.WriteLine($"FAILED: no central version for {id} in Directory.Packages.props");
            throw new Stop();
        }

        // Quiet while it works, but never silent when it does not: swallowing this output once hid an
        // NU1101 behind a bare "exit code 1" in a sibling repository's CI.
        private string Run(string file, params string[] arguments)
        {
            var (code, output) = Exec(file, arguments);
            if (code == 0) return output;
            Console.WriteLine($"FAILED: {string.Join(' ', [file, .. arguments])}");
            foreach (var line in SplitLines(output)) Console.WriteLine("    " + line);
            throw new Stop();
        }

        // For the builds that must fail: the output is kept for the assertions, and a build that succeeds
        // is itself the failure being tested for.
        private string RunFailing(string file, params string[] arguments)
        {
            var (code, output) = Exec(file, arguments);
            if (code != 0) return output;
            Console.WriteLine($"FAILED: expected this to fail, and it succeeded: {string.Join(' ', [file, .. arguments])}");
            foreach (var line in SplitLines(output)) Console.WriteLine("    " + line);
            throw new Stop();
        }

        // stdout and stderr together, in order of arrival, trailing newlines trimmed.
        private (int Code, string Output) Exec(string file, string[] arguments)
        {
            var psi = new ProcessStartInfo(file == "dotnet" ? _dotnet : file) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _cwd };
            foreach (var a in arguments) psi.ArgumentList.Add(a);
            var text = new StringBuilder();
            var gate = new object();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, d) => { if (d.Data is not null) lock (gate) text.Append(d.Data).Append('\n'); };
            p.ErrorDataReceived += (_, d) => { if (d.Data is not null) lock (gate) text.Append(d.Data).Append('\n'); };
            try
            {
                p.Start();
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                return (127, $"{file}: {e.Message}");
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            lock (gate) return (p.ExitCode, text.ToString().TrimEnd('\n'));
        }

        // ── the assertions ─────────────────────────────────────────────────────────────────────────────

        private void AssertContains(string expected, string file, string message)
        {
            var text = Read(file);
            if (text.Contains(expected, StringComparison.Ordinal))
            {
                Console.WriteLine($"  ok: {message}");
                return;
            }
            Console.WriteLine($"  FAIL: {message} (expected '{expected}' in {file})");
            foreach (var line in SplitLines(text).Take(40)) Console.WriteLine("      " + line);
            _failed = true;
        }

        private void AssertNotContains(string unexpected, string file, string message)
        {
            var found = SplitLines(Read(file)).Where(line => line.Contains(unexpected, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0)
            {
                Console.WriteLine($"  ok: {message}");
                return;
            }
            Console.WriteLine($"  FAIL: {message} (did not expect '{unexpected}' in {file})");
            foreach (var line in found.Take(10)) Console.WriteLine("      " + line);
            _failed = true;
        }

        // Every CodoMetis.TypeKit package in the graph resolved at the version just packed.
        private void AssertVersions(string listing)
        {
            var ours = SplitLines(Read(listing)).Where(line => line.Contains("> CodoMetis.TypeKit", StringComparison.Ordinal)).ToList();
            if (ours.Any(line => !line.Contains($" {_version}", StringComparison.Ordinal)))
            {
                Console.WriteLine($"  FAIL: a CodoMetis.TypeKit package resolved at a version other than {_version}");
                foreach (var line in ours) Console.WriteLine("      " + line);
                _failed = true;
            }
            else
            {
                Console.WriteLine($"  ok: every CodoMetis.TypeKit package resolved at {_version}");
            }
        }

        // ── files ──────────────────────────────────────────────────────────────────────────────────────

        // A consumer's source, embedded from Consumers/, written into the current project.
        private void Write(string name, string source)
        {
            using var stream = typeof(Smoke).Assembly.GetManifestResourceNames()
                                            .Where(resource => resource.Replace('\\', '/') == source)
                                            .Select(resource => typeof(Smoke).Assembly.GetManifestResourceStream(resource)!)
                                            .Single();
            using var file = File.Create(Path.Combine(_cwd, name));
            stream.CopyTo(file);
        }

        private void Delete(string name) => File.Delete(Path.Combine(_cwd, name));

        private void Save(string file, string output) => File.WriteAllText(Path.Combine(Work, file), output + "\n");

        private string Read(string file) => File.ReadAllText(Path.Combine(Work, file));

        private void Print(string file)
        {
            foreach (var line in SplitLines(Read(file))) Console.WriteLine("      " + line);
        }

        // The lines of a build's output that name one file, as `grep -F` kept them.
        private static string Lines(string output, string fragment) =>
            string.Join('\n', SplitLines(output).Where(line => line.Contains(fragment, StringComparison.Ordinal)));

        private static IEnumerable<string> SplitLines(string text) =>
            text.Replace("\r\n", "\n").TrimEnd('\n') is { Length: > 0 } trimmed ? trimmed.Split('\n') : [];

        private static string? OnPath(string name)
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [GeneratedRegex(@"warning IL[0-9]{4}")]
        private static partial Regex IlWarning();

        [GeneratedRegex(@"CodoMetis|Shop\.")]
        private static partial Regex PackageOrWoven();
    }
}
