using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Oracles.Tests;

/// <summary>
/// The claim catalogue (<c>test/oracles/claims.json</c>) stays true to the documents it quotes and to the tests it
/// names (design/adr/0112).
/// </summary>
/// <remarks>
/// A claim is a sentence a consumer-facing document states about the library, in a form a test can decide, with the
/// oracle kind, the public members a test of it calls and the test classes that hold it. The catalogue is only worth
/// keeping if it cannot drift: a reworded sentence stales its claim, a renamed test class is found, a surface that no
/// longer exists is noticed. A claim with an empty <c>heldBy</c> is allowed: that is the worklist.
/// </remarks>
public sealed class OracleCatalogueTests
{
    /// <summary>150 at the first catalogue (2026-10-28). A floor, not a target: discovery would otherwise pass on an empty array.</summary>
    private const int KnownClaims = 150;

    private static readonly string[] Kinds = ["claim", "differential", "metamorphic", "exception", "silent-default"];

    [Fact]
    public void The_catalogue_is_not_empty_and_its_ids_are_unique_and_sorted()
    {
        var ids = OracleCatalogue.Claims.Select(claim => claim.Id).ToList();

        ids.Count.ShouldBeGreaterThanOrEqualTo(KnownClaims);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count, "Duplicate claim ids.");
        ids.ShouldBe(ids.Order(StringComparer.Ordinal).ToList(), "Claims are sorted by id.");
        ids.Where(id => !Regex.IsMatch(id, @"^[A-Z]\d{2}-[a-z0-9-]+$"))
           .ShouldBeEmpty("Every id is a letter, two digits, a hyphen and a kebab-case slug.");
    }

    [Fact]
    public void Every_claim_names_an_oracle_kind() =>
        OracleCatalogue.Claims.Where(claim => !Kinds.Contains(claim.Oracle)).Select(claim => $"{claim.Id}: '{claim.Oracle}'")
                       .ShouldBeEmpty("The oracle kind is one of " + string.Join("|", Kinds) + ".");

    [Fact]
    public void Every_claim_quotes_its_source_verbatim()
    {
        var problems = new List<string>();
        var flattened = new Dictionary<string, string>(StringComparer.Ordinal);
        var headings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var claim in OracleCatalogue.Claims)
        {
            if (!File.Exists(Path.Combine(OracleCatalogue.Root, claim.File)))
            {
                problems.Add($"{claim.Id}: source file '{claim.File}' does not exist");
                continue;
            }

            if (claim.Heading is { } heading)
            {
                if (!headings.TryGetValue(claim.File, out var slugs)) headings[claim.File] = slugs = OracleCatalogue.HeadingSlugs(claim.File);
                if (!slugs.Contains(heading)) problems.Add($"{claim.Id}: '{claim.File}' has no heading '#{heading}'");
            }

            if (!flattened.TryGetValue(claim.File, out var text)) flattened[claim.File] = text = OracleCatalogue.Flattened(claim.File);

            var quote = Regex.Replace(claim.Quote, @"\s+", " ").Trim();
            if (quote.Length < 20)
                problems.Add($"{claim.Id}: the quote is too short to pin a sentence: '{quote}'");
            else if (!text.Contains(quote, StringComparison.Ordinal))
                problems.Add($"{claim.Id}: '{claim.File}' no longer contains the quoted sentence; reword the claim or retire it: '{quote}'");
        }

        problems.ShouldBeEmpty("Claims out of step with their documents.");
    }

    [Fact]
    public void Every_claim_names_test_classes_that_exist()
    {
        OracleCatalogue.TestClasses.Count.ShouldBeGreaterThanOrEqualTo(200, "Too few test classes were discovered under test/.");

        OracleCatalogue.Claims.SelectMany(claim => claim.HeldBy.Select(held => (claim.Id, held)))
                       .Where(pair => !OracleCatalogue.TestClasses.ContainsKey(pair.held))
                       .Select(pair => $"{pair.Id} -> {pair.held}")
                       .ShouldBeEmpty("heldBy names a test class that does not exist.");
    }

    [Fact]
    public void Every_claim_names_surfaces_that_exist() =>
        OracleCatalogue.Claims.SelectMany(claim => claim.Surfaces.Select(surface => (claim.Id, surface)))
                       .Where(pair => ApiSurface.Matching(pair.surface).Count == 0)
                       .Select(pair => $"{pair.Id} -> '{pair.surface}'")
                       .ShouldBeEmpty("A claim names a public member that does not exist (keys are TypeName.Member, nested types with '+', '*' is a wildcard).");

    [Fact]
    public void Every_claim_names_at_least_one_surface() =>
        OracleCatalogue.Claims.Where(claim => claim.Surfaces.Count == 0).Select(claim => claim.Id)
                       .ShouldBeEmpty("A claim with no surface cannot be tested.");

    [Fact]
    public void Every_promise_document_carries_a_claim()
    {
        var claimed = OracleCatalogue.Claims.Select(claim => claim.File).ToHashSet(StringComparer.Ordinal);

        OracleCatalogue.PromiseDocuments.Where(document => !claimed.Contains(document))
                       .ShouldBeEmpty("A consumer-facing document has no claim in the catalogue.");
    }

    /// <summary>
    /// heldBy may name any test class, but a class named like an oracle, a sweep over generated cases or a probe over
    /// the product of consumer settings (<c>test/oracles/axes.md</c>), is registered, so that it has a seed.
    /// </summary>
    [Fact]
    public void A_class_named_like_an_oracle_that_holds_a_claim_is_registered()
    {
        var oracles = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);

        OracleCatalogue.Claims.SelectMany(claim => claim.HeldBy).Distinct()
                       .Where(held => held.EndsWith("OracleTests", StringComparison.Ordinal) || held.EndsWith("ParityTests", StringComparison.Ordinal) || held.EndsWith("ProbeTests", StringComparison.Ordinal))
                       .Where(held => !oracles.Contains(held))
                       .ShouldBeEmpty("A class named like an oracle holds a claim but is not in oracles.json, so no seed is required of it.");
    }
}
