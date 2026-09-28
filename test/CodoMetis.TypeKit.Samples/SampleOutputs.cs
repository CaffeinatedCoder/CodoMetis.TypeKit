using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ReadmeSamples;

/// <summary>What the tests beside the samples need to check an output a sample states in a comment.</summary>
/// <remarks>
/// The expected value is read from the README, from the comment on the line whose code is the expression the
/// test evaluates (<c>[CallerArgumentExpression]</c>), never written into the test a second time.
/// A test that pinned the output itself would, after an upgrade changed it, be updated and leave the README
/// stating the old output; this way the README's comment is the assertion. ReadmeSampleTests holds the README
/// block to the sample, so the line the test finds is the line the sample compiles.
/// </remarks>
internal static partial class SampleOutputs
{
    /// <summary>An in-memory host's OpenAPI document; <paramref name="configure"/> calls <c>AddOpenApi</c>, as a sample does.</summary>
    public static async Task<JsonNode> OpenApiDocumentAsync(Action<WebApplicationBuilder> configure, Action<WebApplication> endpoints)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        configure(builder);

        await using var app = builder.Build();
        app.MapOpenApi();
        endpoints(app);

        await app.StartAsync(TestContext.Current.CancellationToken);
        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        return JsonNode.Parse(json)!;
    }

    /// <summary>The component a document describes under <paramref name="name"/>.</summary>
    public static JsonNode Component(this JsonNode document, string name) =>
        document["components"]?["schemas"]?[name]
     ?? throw new InvalidOperationException($"The document has no component '{name}': {document["components"]?.ToJsonString()}");

    /// <summary>
    /// The <c>WHERE</c> clause of the SQL a query becomes, on one line: a subquery's indented lines are
    /// joined to it. <c>ToQueryString</c> needs no database.
    /// </summary>
    public static string WhereClause(this IQueryable query)
    {
        var lines = query.ToQueryString().Split('\n');
        var where = Array.FindIndex(lines, line => line.StartsWith("WHERE", StringComparison.Ordinal));
        if (where < 0) throw new InvalidOperationException($"The query has no WHERE clause: {query.ToQueryString()}");

        var clause = lines.Skip(where).TakeWhile((line, i) => i == 0 || line.StartsWith(' ')).Select(line => line.Trim());

        return string.Join(' ', clause).Replace("( ", "(", StringComparison.Ordinal);
    }

    /// <summary>Options for PostgreSQL SQL text only: nothing opens the connection.</summary>
    public static DbContextOptions<TContext> NpgsqlWithoutServer<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseNpgsql(ConnectionStringWithoutServer).UseTypeKit().Options;

    public const string ConnectionStringWithoutServer = "Host=localhost;Database=none";

    /// <summary>A README's text, from the repository root found by its solution file.</summary>
    public static string Readme(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.TypeKit.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, path)).ReplaceLineEndings("\n");
        }

        throw new InvalidOperationException($"No CodoMetis.TypeKit.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>The query's SQL is what the README's comment on the same query says.</summary>
    public static void ShouldBeTheSqlShownIn(this IQueryable query, string readme, [CallerArgumentExpression(nameof(query))] string expression = "") =>
        query.WhereClause().ShouldBe(Shown(readme, expression), $"{readme}: {expression}");

    /// <summary>The value, rendered as the README writes it, is the output its comment states: the comment up to any ", ".</summary>
    public static void ShouldBeShownIn<T>(this T actual, string readme, Func<T, string> render, [CallerArgumentExpression(nameof(actual))] string expression = "") =>
        render(actual).ShouldBe(StatedOutput(Shown(readme, expression)), $"{readme}: {expression}");

    /// <summary>
    /// A comment of the form "{exception} naming {a} and {b}, …": the call throws that exception, whose message
    /// names both, and never <paramref name="input"/>, the refused text.
    /// </summary>
    public static void ShouldRefuseAsShownIn(Func<object?> call, string readme, string input, [CallerArgumentExpression(nameof(call))] string expression = "")
    {
        expression = expression.StartsWith("() => ", StringComparison.Ordinal) ? expression["() => ".Length..] : expression;
        var comment = Shown(readme, expression);
        var claim = RefusalClaim().Match(comment);
        claim.Success.ShouldBeTrue($"{readme}: the comment on '{expression}' should read '{{exception}} naming {{a}} and {{b}}', but is '{comment}'.");

        Exception? thrown = null;
        try { call(); }
        catch (Exception exception) { thrown = exception; }

        thrown.ShouldNotBeNull($"{readme}: {expression} should throw {claim.Groups["exception"].Value}.");
        thrown.GetType().Name.ShouldBe(claim.Groups["exception"].Value, $"{readme}: {expression}");
        thrown.Message.ShouldContain(claim.Groups["first"].Value, Case.Sensitive, $"{readme}: {expression}");
        thrown.Message.ShouldContain(claim.Groups["second"].Value, Case.Sensitive, $"{readme}: {expression}");
        thrown.Message.ShouldNotContain(input, Case.Sensitive, $"{readme}: {expression}");
    }

    /// <summary>
    /// What a README says a line of its C# shows: the comment after the line, or else the comment lines right
    /// below it. The line is the README's one line of code that is <c>{expression};</c> or ends in
    /// <c> = {expression};</c>.
    /// </summary>
    public static string Shown(string readme, string expression)
    {
        expression = Unparenthesized(expression);
        var lines = Readme(readme).Split('\n');
        var matches = Enumerable.Range(0, lines.Length)
                                .Where(i => CodeOf(lines[i]) is var code && (code == $"{expression};" || code.EndsWith($" = {expression};", StringComparison.Ordinal)))
                                .ToArray();

        if (matches.Length != 1)
            throw new ShouldAssertException($"{readme} should have one line of code '{expression};' or '… = {expression};', but has {matches.Length}.");

        if (CommentOf(lines[matches[0]]) is { } comment) return comment;

        var below = lines.Skip(matches[0] + 1)
                         .TakeWhile(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                         .Select(line => line.TrimStart()[2..].Trim())
                         .ToArray();

        return below.Length > 0 ? string.Join(' ', below) : throw new ShouldAssertException($"{readme}:{matches[0] + 1} states nothing about '{expression}'.");
    }

    /// <summary>The comment on each property of a class a README declares, by property name.</summary>
    public static IReadOnlyDictionary<string, string> PropertyComments(string readme, string declaration)
    {
        var lines = Readme(readme).Split('\n');
        var start = Array.IndexOf(lines, declaration);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{readme} has no line '{declaration}'.");

        return lines.Skip(start + 1)
                    .TakeWhile(line => line != "}")
                    .Select(line => (Property: PropertyName().Match(CodeOf(line)), Comment: CommentOf(line)))
                    .Where(line => line.Property.Success && line.Comment is not null)
                    .ToDictionary(line => line.Property.Groups["name"].Value, line => line.Comment!);
    }

    /// <summary>The output a comment states, before any commentary after a ", ".</summary>
    private static string StatedOutput(string comment) =>
        comment.IndexOf(", ", StringComparison.Ordinal) is var comma and >= 0 ? comment[..comma] : comment;

    private static string CodeOf(string line) => (CommentStart(line) is var start and >= 0 ? line[..start] : line).Trim();

    private static string? CommentOf(string line) => CommentStart(line) is var start and >= 0 ? line[(start + 2)..].Trim() : null;

    /// <summary>Where a line's <c>//</c> comment starts, outside any string literal.</summary>
    private static int CommentStart(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (inString && line[i] == '\\') i++;
            else if (line[i] == '"') inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/') return i;
        }

        return -1;
    }

    /// <summary><c>(a == b)</c> as a README line writes it after <c>bool same =</c>: <c>a == b</c>.</summary>
    private static string Unparenthesized(string expression)
    {
        if (!expression.StartsWith('(') || !expression.EndsWith(')')) return expression;

        var depth = 0;
        for (var i = 0; i < expression.Length - 1; i++)
        {
            depth += expression[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0) return expression;
        }

        return expression[1..^1];
    }

    [GeneratedRegex(@"^(?<exception>\w+) naming (?<first>\w+) and (?<second>\w+)\b")]
    private static partial Regex RefusalClaim();

    [GeneratedRegex(@"(?<name>\w+)\s*\{\s*get;")]
    private static partial Regex PropertyName();

    /// <summary>Holds when every key <paramref name="expected"/> has, <paramref name="actual"/> has with the same value, recursively.</summary>
    public static void ShouldContainSchema(this JsonNode? actual, JsonNode expected, string path)
    {
        if (expected is JsonObject expectedObject)
        {
            if (actual is not JsonObject actualObject)
                throw new ShouldAssertException($"{path}: expected an object like {expected.ToJsonString()}, but was {actual?.ToJsonString() ?? "missing"}");

            foreach (var (key, value) in expectedObject) actualObject[key].ShouldContainSchema(value!, $"{path}.{key}");
        }
        else if (!JsonNode.DeepEquals(actual, expected))
        {
            throw new ShouldAssertException($"{path}: expected {expected.ToJsonString()}, but was {actual?.ToJsonString() ?? "missing"}");
        }
    }
}
