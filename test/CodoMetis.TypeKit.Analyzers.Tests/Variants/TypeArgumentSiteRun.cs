using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests.Variants;

/// <summary>Runs one site of <see cref="TypeArgumentSites"/> for one subject of a rule.</summary>
internal static partial class TypeArgumentSiteRun
{
    /// <summary>Every subject crossed with every site, each with the breaking argument and with the meeting one.</summary>
    public static TheoryData<string, string, bool> Cases(IEnumerable<TypeArgumentSubject> subjects)
    {
        var cases = new TheoryData<string, string, bool>();

        foreach (var subject in subjects)
        {
            foreach (var site in TypeArgumentSites.All)
            {
                cases.Add(subject.Name, site.Name, true);
                cases.Add(subject.Name, site.Name, false);
            }
        }

        return cases;
    }

    /// <summary>
    /// A breaking argument at a <see cref="Reach.Use"/> site is reported once, at the site's span, with
    /// <paramref name="id"/>; anything else, the meeting argument everywhere and a site the subject names
    /// as silent, is reported nowhere.
    /// </summary>
    public static Task Verify<TAnalyzer>(TypeArgumentSubject subject, string siteName, bool breaking, string id, DiagnosticSeverity severity)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var site      = TypeArgumentSites.All.Single(candidate => candidate.Name == siteName);
        var argument  = breaking ? subject.Breaking : subject.Meeting;
        var silenced  = subject.SilentAt?.ContainsKey(site.Name) ?? false;
        var reported  = breaking && site.Reach == Reach.Use && !silenced;
        var use       = Fill(site.Use, subject, argument);

        var test = new AnalyzerTest<TAnalyzer>(Fill(TypeArgumentSites.Subjects + subject.Declarations, subject, argument));
        test.TestBehaviors = VariantSweep.Behaviors;
        test.TestState.Sources.Add((site.File, reported ? use : Unmarked(use)));

        if (reported)
            test.ExpectedDiagnostics.Add(new DiagnosticResult(id, severity).WithLocation(0));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static string Fill(string source, TypeArgumentSubject subject, string argument) =>
        source.Replace("{C}", subject.Constraint).Replace("{A}", argument);

    private static string Unmarked(string source) => Location().Replace(source, "$1");

    [GeneratedRegex(@"\{\|#0:(.*?)\|\}", RegexOptions.Singleline)]
    private static partial Regex Location();
}
