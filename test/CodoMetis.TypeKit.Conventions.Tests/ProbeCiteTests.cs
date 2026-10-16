using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Conventions.Tests;

/// <summary>
/// Every reason a check gives for not applying or for meeting a refusal is cited, and what it cites exists: in the
/// probe corpus, the first argument of a check's <c>Inapplicable</c> or <c>Refused</c> is a <c>Cite</c> factory
/// (<c>NotPromised</c>, <c>Docs</c>, <c>Fact</c>, <c>Record</c>) called with a string literal, and a
/// <c>NotPromised</c> id is a not-promised entry of the probe's claim (of any claim, for a harness source no one
/// probe owns), a <c>Docs</c> source names a file of the repository, a <c>Fact</c> an entry under design/knowledge/
/// and a <c>Record</c> a file under design/adr/.
/// </summary>
/// <remarks>
/// The factories check the form when a check runs; this holds, before any build, that an excuse names a reason a
/// reader can find. `prober register` and `publish` hold the same for the staged sources (SourceLint). It reads
/// syntax, so a call in a comment or a string does not count, and the positive control finds each rule's defect. No
/// floor yet: no probe calls a check's <c>Inapplicable</c> or <c>Refused</c> until the conversion that starts
/// with the per-check evidence.
/// </remarks>
public sealed class ProbeCiteTests
{
    private static string Corpus => Path.Combine(Repository.Root, "tools", "probe", "corpus");

    [Fact]
    public void Every_citation_in_the_corpus_is_a_factory_with_a_literal_naming_what_exists()
    {
        var context = Context.Of(Repository.Root);
        Directory.EnumerateFiles(Corpus, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                 .SelectMany(path => Problems(Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'), File.ReadAllText(path), context))
                 .ShouldBeEmpty();
    }

    [Fact]
    public void A_citation_built_from_text_or_naming_nothing_is_found()
    {
        const string source =
            """
            class P1Probe
            {
                string? Check(Check c, string x, Entry e)
                {
                    var refused = e.Refused();
                    c.Inapplicable(Cite.NotPromised("c1-limit"), x);
                    c.Refused(Cite.Docs("README.md#what-typekit-guarantees"));
                    c.Refused(Cite.Fact("KB-001"));
                    c.Inapplicable(Cite.Record("0094"), "detail");
                    var cite = Cite.NotPromised("c1-limit");
                    c.Inapplicable(cite);
                    c.Refused(Cite.NotPromised("c1-" + x));
                    c.Inapplicable(Cite.NotPromised("c2-limit"));
                    c.Refused(Cite.Docs("docs/gone.md#a"));
                    c.Refused(Cite.Fact("KB-999"));
                    c.Inapplicable(Cite.Record("9999"));
                    // c.Inapplicable(cite);
                    var text = "c.Inapplicable(cite)";
                    return null;
                }
            }
            """;
        var context = new Context(Repository.Root,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["P1Probe"] = "C1" },
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["C1"] = ["c1-limit"], ["C2"] = ["c2-limit"] });
        Problems("probes/P1Probe.cs", source, context).ShouldBe(
        [
            "probes/P1Probe.cs:11: Inapplicable(cite, ...) does not cite by a Cite factory with a string literal",
            "probes/P1Probe.cs:12: Refused(Cite.NotPromised(\"c1-\" + x), ...) does not cite by a Cite factory with a string literal",
            "probes/P1Probe.cs:13: Inapplicable(Cite.NotPromised(\"c2-limit\"), ...) names no not-promised entry of its claim C1",
            "probes/P1Probe.cs:14: Refused(Cite.Docs(\"docs/gone.md#a\"), ...) names no file docs/gone.md in the repository",
            "probes/P1Probe.cs:15: Refused(Cite.Fact(\"KB-999\"), ...) names no entry under design/knowledge/",
            "probes/P1Probe.cs:16: Inapplicable(Cite.Record(\"9999\"), ...) names no record under design/adr/",
        ]);
        Problems("harness/Shared.cs", source.Replace("P1Probe", "Shared"), context)
            .ShouldNotContain(p => p.Contains("c2-limit"), "a harness source no one probe owns may cite any claim's entry");
    }

    /// <summary>The repository's files, each claim's not-promised ids, and the claim of each probe by its name and by the class its filter selects.</summary>
    private sealed record Context(string Root, Dictionary<string, string> ClaimOf, Dictionary<string, HashSet<string>> NotPromised)
    {
        public static Context Of(string root)
        {
            var corpus = Path.Combine(root, "tools", "probe", "corpus");
            var notPromised = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var claim in JsonNode.Parse(File.ReadAllText(Path.Combine(corpus, "claims.json")))!.AsArray().OfType<JsonObject>())
                notPromised[claim["id"]!.GetValue<string>()] = [.. (claim["not-promised"] as JsonArray ?? []).Select(n => n!["id"]!.GetValue<string>())];
            var claimOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, entry) in JsonNode.Parse(File.ReadAllText(Path.Combine(corpus, "registry.json")))!["probes"]!.AsObject())
            {
                var claim = entry!["claim"]!.GetValue<string>();
                claimOf[name] = claim;
                if (entry["filter"]?.GetValue<string>().TrimStart('*') is { Length: > 0 } cls) claimOf.TryAdd(cls, claim);
            }
            return new Context(root, claimOf, notPromised);
        }
    }

    private static IEnumerable<string> Problems(string path, string text, Context context)
    {
        var claim = context.ClaimOf.GetValueOrDefault(Path.GetFileNameWithoutExtension(path));
        foreach (var call in CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var method and ("Inapplicable" or "Refused") }) continue;
            var arguments = call.ArgumentList.Arguments;
            if (arguments.Count == 0) continue;   // an entry's own Refused() of the harness, not a check's
            var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var shown = $"{path}:{line}: {method}({arguments[0]}, ...)";
            if (arguments[0].Expression is not InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Cite" }, Name.Identifier.ValueText: var factory and ("NotPromised" or "Docs" or "Fact" or "Record") },
                    ArgumentList.Arguments: [{ Expression: LiteralExpressionSyntax literal }],
                } || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                yield return $"{shown} does not cite by a Cite factory with a string literal";
                continue;
            }
            var value = literal.Token.ValueText;
            var missing = factory switch
            {
                "NotPromised" when claim is not null => context.NotPromised.GetValueOrDefault(claim)?.Contains(value) ?? false ? null : $"names no not-promised entry of its claim {claim}",
                "NotPromised" => context.NotPromised.Values.Any(ids => ids.Contains(value)) ? null : "names no not-promised entry of any claim",
                "Docs" => value.Split('#')[0] is var file && file.Length > 0 && !file.Contains("..", StringComparison.Ordinal) && File.Exists(Path.Combine(context.Root, file))
                    ? null : $"names no file {value.Split('#')[0]} in the repository",
                "Fact" => Named(Path.Combine(context.Root, "design", "knowledge"), value) ? null : "names no entry under design/knowledge/",
                _ => Named(Path.Combine(context.Root, "design", "adr"), value) ? null : "names no record under design/adr/",
            };
            if (missing is not null) yield return $"{shown} {missing}";
        }
    }

    private static bool Named(string directory, string id) =>
        id.Length > 0 && Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*.md").Any(f => Path.GetFileName(f).StartsWith(id + "-", StringComparison.Ordinal) || Path.GetFileName(f) == id + ".md");
}
