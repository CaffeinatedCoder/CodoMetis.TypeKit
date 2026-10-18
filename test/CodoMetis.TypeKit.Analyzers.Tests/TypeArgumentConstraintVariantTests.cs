using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0010 across the variant catalogue for a type argument (<see cref="TypeArgumentSites"/>): every
/// shape through which an argument reaches a parameter, with an argument that breaks the attribute and
/// one that meets it.
/// </summary>
/// <remarks>
/// Two kinds 2.0.0 shipped, <c>[Sealed]</c> and <c>[Concrete]</c>, and one 2.1.0 added,
/// <c>[ReadOnlyStruct]</c>, since the two differ at a method bound by pattern (decision 89).
/// </remarks>
public sealed class TypeArgumentConstraintVariantTests
{
    private const string Candidates =
        """
        public abstract class Handler { }
        public interface IHandler { }
        public sealed class OrderHandler : Handler, IHandler { }
        public readonly struct Frozen { }

        """;

    /// <summary>A 2.0.0 kind waits for 3.0.0 at a method bound by pattern, since an Error that reports more waits for a major version.</summary>
    private static readonly Dictionary<string, string> PatternWaitsForThree = new()
    {
        ["a foreach's GetEnumerator"] = "decision 89",
        ["an await's GetAwaiter"] = "decision 89",
        ["a deconstruction"] = "decision 89",
        ["a positional pattern"] = "decision 89",
        ["a collection expression's Add"] = "decision 89",
    };

    private static readonly TypeArgumentSubject[] Subjects =
    [
        new("[Sealed]", "[Sealed]", "Handler", "OrderHandler", Candidates, PatternWaitsForThree),
        new("[Concrete]", "[Concrete]", "IHandler", "OrderHandler", Candidates, PatternWaitsForThree),
        new("[ReadOnlyStruct]", "[ReadOnlyStruct]", "Handler", "Frozen", Candidates),
    ];

    public static TheoryData<string, string, bool> Cases() => TypeArgumentSiteRun.Cases(Subjects);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Each_site_reports_a_breaking_argument_once_and_a_meeting_one_never(string subject, string site, bool breaking) =>
        TypeArgumentSiteRun.Verify<TypeArgumentConstraintAnalyzer>(
            Subjects.Single(candidate => candidate.Name == subject), site, breaking, "CMTK0010", DiagnosticSeverity.Error);

    [Fact]
    public void The_catalogue_is_not_empty_and_its_site_names_are_unique()
    {
        TypeArgumentSites.All.Count.ShouldBeGreaterThanOrEqualTo(50);
        TypeArgumentSites.All.Select(site => site.Name).ShouldBeUnique();
        PatternWaitsForThree.Keys.ShouldAllBe(name => TypeArgumentSites.All.Any(site => site.Name == name));
    }
}
