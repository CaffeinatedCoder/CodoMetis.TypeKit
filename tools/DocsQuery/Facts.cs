using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocsQuery;

/// <summary>
/// What the checkout installs, as a fact's pins name it: each package's version from
/// <c>Directory.Packages.props</c>, and the SDK from <c>global.json</c>.
/// </summary>
internal sealed record Installed(IReadOnlyDictionary<string, string> Packages, string? Sdk)
{
    public const string PackagesFile = "Directory.Packages.props", SdkFile = "global.json";

    public static Installed Read(string root)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var props    = Path.Combine(root, PackagesFile);

        if (File.Exists(props))
        {
            foreach (var element in XDocument.Load(props).Descendants().Where(element => element.Name.LocalName == "PackageVersion"))
            {
                if (element.Attribute("Include")?.Value is { } id && element.Attribute("Version")?.Value is { } version) packages[id] = version;
            }
        }

        string? sdk  = null;
        var     json = Path.Combine(root, SdkFile);

        if (File.Exists(json))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(json), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.TryGetProperty("sdk", out var section) && section.TryGetProperty("version", out var version)) sdk = version.GetString();
        }

        return new Installed(packages, sdk);
    }
}

/// <summary>
/// A fact's pin against what is installed. A pin is <c>{PackageId} {version}</c> for a package of
/// <c>Directory.Packages.props</c>, or <c>sdk {band}</c> (<c>sdk 10.0.4xx</c>) for the SDK <c>global.json</c>
/// pins, whose band is what <c>rollForward: latestPatch</c> holds, or <c>external {name} {version}</c> for
/// something this checkout never installs (a competitor's release), which is never compared. Anything after the
/// version is a comment.
/// </summary>
internal static partial class Pins
{
    internal enum State { Current, Behind, Ahead, NotInstalled, External, Malformed }

    public static (State State, string Detail) Compare(string pin, Installed installed)
    {
        var parts = pin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return (State.Malformed, $"'{pin}' is no pin: write '{{PackageId}} {{version}}' or 'sdk {{band}}', such as 'sdk 10.0.4xx'");

        var (name, version) = (parts[0], parts[1]);

        if (name == "external")
            return parts.Length >= 3 ? (State.External, $"{parts[1]} {parts[2]}, not installed here: never compared") : (State.Malformed, $"'{pin}' is no pin: write 'external {{name}} {{version}}'");

        if (name == "sdk")
        {
            if (installed.Sdk is null) return (State.NotInstalled, $"{Installed.SdkFile} pins no SDK");
            if (Band().Match(version) is not { Success: true } band) return (State.Malformed, $"'{version}' is no SDK band: write it as 10.0.4xx");
            if (Version.TryParse(installed.Sdk, out var sdk) is false) return (State.Malformed, $"{Installed.SdkFile}'s SDK '{installed.Sdk}' is no version");

            var installedBand = $"{sdk.Major}.{sdk.Minor}.{sdk.Build / 100}xx";
            var order = CompareVersions($"{band.Groups["major"].Value}.{band.Groups["minor"].Value}.{band.Groups["band"].Value}", $"{sdk.Major}.{sdk.Minor}.{sdk.Build / 100}");
            return Verdict(order, $"sdk {version}", $"{installedBand} ({Installed.SdkFile}: {installed.Sdk})");
        }

        return installed.Packages.TryGetValue(name, out var current)
            ? Verdict(CompareVersions(version, current), $"{name} {version}", current)
            : (State.NotInstalled, $"{name} is in neither {Installed.PackagesFile} nor {Installed.SdkFile}");

        static (State, string) Verdict(int order, string pinned, string current) => order switch
        {
            0   => (State.Current, $"{pinned}, installed"),
            < 0 => (State.Behind, $"pinned {pinned}, installed {current}: re-verify on {current}"),
            _   => (State.Ahead, $"pinned {pinned}, installed {current}: verified on a version this checkout does not run"),
        };
    }

    /// <summary>
    /// An <c>external {name} {version}</c> pin's name and version, where the version is one (<c>10.6.0</c>,
    /// <c>5.0.0-beta-78</c>), or null: another kind of pin, or an external one dated or described rather than
    /// versioned (<c>external NuGet registration data</c>). A dated pin, <c>external GitHub-Actions 2026-10-29</c>,
    /// reads as a version: that NuGet has no package of the name is what keeps it from being compared.
    /// </summary>
    public static (string Name, string Version)? External(string pin)
    {
        var parts = pin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is ["external", var name, var version, ..] && VersionText().IsMatch(version) ? (name, version) : null;
    }

    /// <summary>Whether a version is a pre-release: a label after a dash.</summary>
    public static bool IsPreRelease(string version) => version.Contains('-', StringComparison.Ordinal);

    /// <summary>
    /// Orders two versions: numeric parts numerically, then a release after any of its pre-releases, and
    /// pre-release labels as SemVer 2 and NuGet order them. Enough for the versions <c>Directory.Packages.props</c>
    /// holds and the ones NuGet lists.
    /// </summary>
    public static int CompareVersions(string left, string right)
    {
        var (leftCore, leftLabel)   = Split(left);
        var (rightCore, rightLabel) = Split(right);

        for (var i = 0; i < Math.Max(leftCore.Length, rightCore.Length); i++)
        {
            var a = i < leftCore.Length ? leftCore[i] : "0";
            var b = i < rightCore.Length ? rightCore[i] : "0";
            var order = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x) && long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                ? x.CompareTo(y)
                : string.CompareOrdinal(a, b);
            if (order != 0) return Math.Sign(order);
        }

        return (leftLabel, rightLabel) switch
        {
            (null, null) => 0,
            (null, _)    => 1,
            (_, null)    => -1,
            _            => Labels(leftLabel, rightLabel),
        };

        // Dot-separated identifiers: numeric ones numerically and before an alphanumeric one, alphanumeric ones
        // ignoring case, and a label before a longer one it starts (beta.2 before beta.10, beta before beta.1).
        static int Labels(string left, string right)
        {
            var (a, b) = (left.Split('.'), right.Split('.'));

            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var order = (long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x), long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y)) switch
                {
                    (true, true)  => x.CompareTo(y),
                    (true, false) => -1,
                    (false, true) => 1,
                    _             => string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase),
                };
                if (order != 0) return Math.Sign(order);
            }

            return a.Length.CompareTo(b.Length);
        }

        static (string[] Core, string? Label) Split(string version)
        {
            var dash = version.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? (version.Split('.'), null) : (version[..dash].Split('.'), version[(dash + 1)..]);
        }
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<band>\d)xx$")]
    private static partial Regex Band();

    [GeneratedRegex(@"^\d+(\.\d+)*(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex VersionText();
}

/// <summary>
/// The facts due for a re-read before a release (CONTRIBUTING's "Releasing"). An <c>external</c> pin names what this
/// checkout never installs, a competitor's release among them, so <c>check</c> never compares it and the fact is as
/// fresh as its date. A fact with one is due when it was verified more than a number of days ago and, when a feed is
/// given, when NuGet has a later release of a package it pins: a later stable release for a stable pin, a later one
/// of any kind for a pre-release. Run by hand, never in CI: a build must not fail because time passed or someone
/// else released.
/// </summary>
internal static class FactReview
{
    /// <summary>Six months: a fact read longer ago than this is due, whatever its pins.</summary>
    public const int DefaultDays = 183;

    internal sealed record Due(string At, string Kind, string Id, string Target, string Detail);

    /// <summary>The versions NuGet lists for a package id, or null where it has no package of that id.</summary>
    internal delegate IReadOnlyList<string>? Feed(string packageId);

    public static IReadOnlyList<Due> List(DocIndex index, DateOnly today, int days, Feed? feed, out int facts, out int compared)
    {
        var due   = new List<Due>();
        var order = Comparer<string>.Create(Pins.CompareVersions);
        (facts, compared) = (0, 0);

        foreach (var doc in index.Docs.Where(doc => doc.Kind == Kind.Fact))
        {
            var pins = doc.Meta.Where(field => field.Key == "pins" && field.Value.StartsWith("external ", StringComparison.Ordinal)).ToList();
            if (pins.Count == 0) continue;
            facts++;

            string At(int line) => $"{doc.Path}:{line.ToString(CultureInfo.InvariantCulture)}";

            if (doc.Meta.FirstOrDefault(field => field.Key == "verified") is { } verified
             && DateOnly.TryParseExact(verified.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
             && today.DayNumber - date.DayNumber is var age && age > days)
                due.Add(new Due(At(verified.Line), "fact-old", doc.Id!, verified.Value, $"verified {age.ToString(CultureInfo.InvariantCulture)} days ago, more than {days.ToString(CultureInfo.InvariantCulture)}: re-read it at its latest release"));

            if (feed is null) continue;

            foreach (var pin in pins)
            {
                if (Pins.External(pin.Value) is not { } external || feed(external.Name) is not { Count: > 0 } versions) continue;
                var (name, version) = external;
                compared++;

                var track  = Pins.IsPreRelease(version) ? versions : [.. versions.Where(listed => !Pins.IsPreRelease(listed))];
                var latest = track.Count == 0 ? null : track.Max(order);

                if (latest is not null && Pins.CompareVersions(version, latest) < 0)
                    due.Add(new Due(At(pin.Line), "fact-released", doc.Id!, pin.Value, $"NuGet's latest{(Pins.IsPreRelease(version) ? "" : " stable")} {name} is {latest}: re-read it there"));
            }
        }

        return due;
    }
}

/// <summary>
/// The rules a verified fact is held to (design/adr/0093): its frontmatter is complete and plain, each pin
/// matches what is installed, and its <c>test:</c> names the characterization test that observes it, or says
/// <c>none</c> and why. A test of the facts project that no fact names is a problem too. A <c>fixture:</c>, the
/// measured answer of the fact's test as data, sits beside that test and exists (design/adr/0107).
/// </summary>
internal static partial class FactRules
{
    public const string FactsProject = "test/CodoMetis.TypeKit.Facts";

    public static readonly string[] Required = ["verified", "pins", "source", "turned-on", "test"];

    internal sealed record Problem(string At, string Kind, string Target, string Detail);

    public const string FixtureSuffix = ".fixture.json";

    public static IReadOnlyList<Problem> Check(DocIndex index, string root, out int facts)
    {
        var problems  = new List<Problem>();
        var installed = Installed.Read(root);
        var named     = new HashSet<string>(StringComparer.Ordinal);
        var fixtures  = new HashSet<string>(StringComparer.Ordinal);
        // Why each fact cannot settle a pruning or an excuse, or null where its test exists; and its fixture, where one exists.
        var notExecutable = new Dictionary<string, string?>(StringComparer.Ordinal);
        facts = 0;

        for (var d = 0; d < index.Docs.Count; d++)
        {
            var doc = index.Docs[d];
            if (doc.Kind != Kind.Fact) continue;
            facts++;

            string At(int line) => $"{doc.Path}:{line.ToString(CultureInfo.InvariantCulture)}";

            foreach (var key in Required.Where(key => doc.Meta.All(field => field.Key != key)))
                problems.Add(new Problem(At(1), "fact-incomplete", doc.Id!, $"no '{key}:' in the frontmatter"));

            foreach (var field in doc.Meta.Where(field => Required.Contains(field.Key)))
            {
                if (NotPlain(field.Value) is { } why) problems.Add(new Problem(At(field.Line), "fact-frontmatter", field.Key, why));
            }

            if (doc.Get("verified") is { } verified && !DateOnly.TryParseExact(verified, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                problems.Add(new Problem(At(doc.Meta.First(field => field.Key == "verified").Line), "fact-incomplete", "verified", $"'{verified}' is no date: write YYYY-MM-DD"));

            foreach (var pin in doc.Meta.Where(field => field.Key == "pins"))
            {
                var (state, detail) = Pins.Compare(pin.Value, installed);
                if (state is Pins.State.Current or Pins.State.External) continue;

                problems.Add(new Problem(At(pin.Line), state switch
                {
                    Pins.State.Behind       => "fact-behind",
                    Pins.State.Ahead        => "fact-ahead",
                    Pins.State.NotInstalled => "fact-not-installed",
                    _                       => "fact-bad-pin",
                }, pin.Value, detail));
            }

            var className = doc.Id!.Replace("-", "", StringComparison.Ordinal);
            notExecutable[doc.Id!] = "it has no test: line";

            if (doc.Meta.FirstOrDefault(field => field.Key == "test") is { } test)
            {
                if (test.Value.StartsWith("none, ", StringComparison.Ordinal) && test.Value.Length > "none, ".Length)
                {
                    notExecutable[doc.Id!] = $"its test: is '{test.Value}'";
                }
                else if (TestPath(test.Value) is var path && !path.StartsWith(FactsProject + "/", StringComparison.Ordinal))
                {
                    problems.Add(new Problem(At(test.Line), "fact-untested", test.Value, $"write the path of its test in {FactsProject}/, or 'none, ' and why no test can observe it"));
                    notExecutable[doc.Id!] = $"its test: '{test.Value}' is no test of {FactsProject}/";
                }
                else
                {
                    named.Add(path);
                    var file = Path.Combine(root, path);

                    if (!File.Exists(file))
                    {
                        problems.Add(new Problem(At(test.Line), "fact-untested", path, "the file does not exist"));
                        notExecutable[doc.Id!] = $"its test {path} does not exist";
                    }
                    else if (!Regex.IsMatch(File.ReadAllText(file), $@"\bclass\s+{className}\b"))
                    {
                        problems.Add(new Problem(At(test.Line), "fact-untested", path, $"declares no class {className}, the test named by the fact's id"));
                        notExecutable[doc.Id!] = $"its test {path} declares no class {className}";
                    }
                    else
                    {
                        notExecutable[doc.Id!] = null;
                    }
                }
            }

            if (doc.Meta.FirstOrDefault(field => field.Key == "fixture") is { } fixture)
            {
                var expected = $"{FactsProject}/{className}{FixtureSuffix}";
                fixtures.Add(fixture.Value);

                if (NotPlain(fixture.Value) is { } why)
                    problems.Add(new Problem(At(fixture.Line), "fact-frontmatter", fixture.Key, why));
                else if (notExecutable[doc.Id!] is { } untested)
                    problems.Add(new Problem(At(fixture.Line), "fact-fixture", fixture.Value, $"a fixture is the measured answer of the fact's test, and {untested}"));
                else if (fixture.Value != expected)
                    problems.Add(new Problem(At(fixture.Line), "fact-fixture", fixture.Value, $"write it beside the test, as {expected}"));
                else if (!File.Exists(Path.Combine(root, fixture.Value)))
                    problems.Add(new Problem(At(fixture.Line), "fact-fixture-missing", fixture.Value, "the file does not exist"));
            }
        }

        var project = Path.Combine(root, FactsProject);
        if (Directory.Exists(project))
        {
            foreach (var path in Directory.EnumerateFiles(project, "KB*.cs", SearchOption.TopDirectoryOnly).Select(path => Corpus.Relative(root, path)).Order(StringComparer.Ordinal))
            {
                if (TestFile().IsMatch(Path.GetFileName(path)) && !named.Contains(path))
                    problems.Add(new Problem($"{path}:1", "fact-orphan-test", path, "no fact in design/knowledge/ names this test"));
            }

            foreach (var path in Directory.EnumerateFiles(project, "KB*" + FixtureSuffix, SearchOption.TopDirectoryOnly).Select(path => Corpus.Relative(root, path)).Order(StringComparer.Ordinal))
            {
                if (!fixtures.Contains(path))
                    problems.Add(new Problem($"{path}:1", "fact-orphan-fixture", path, "no fact in design/knowledge/ names this fixture"));
            }
        }

        return problems;
    }

    /// <summary>
    /// The test's path, without a note in parentheses after it, such as where the test runs:
    /// <c>test/CodoMetis.TypeKit.Facts/KB028.cs (runs only with TYPEKIT_PG set, until WS6's CI service)</c>.
    /// </summary>
    private static string TestPath(string value) =>
        value.EndsWith(')') && value.IndexOf(" (", StringComparison.Ordinal) is > 0 and var note ? value[..note] : value;

    /// <summary>Why a frontmatter value would not read back as the plain YAML string it is meant to be, or null.</summary>
    private static string? NotPlain(string value) =>
        value.Length > 0 && "`\"'[]{}*&!|>%@#".Contains(value[0], StringComparison.Ordinal) ? $"a value starting with '{value[0]}' is not a plain YAML string"
      : value.Contains(": ", StringComparison.Ordinal) ? "': ' inside a value is not plain YAML; use a comma or a dash"
      : value.Contains(" #", StringComparison.Ordinal) ? "' #' starts a YAML comment"
      : null;

    [GeneratedRegex(@"^KB\d{3}\.cs$")]
    private static partial Regex TestFile();
}
