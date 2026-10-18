using System.Reflection;

namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// The fixtures beside the facts' tests (design/adr/0107): each one the build copies loads, names the fact its file is
/// named for, and has that fact's test class here; the reader the harness will use covers, compares and refuses as
/// <see cref="FactFixture"/> says.
/// </summary>
public sealed class FixtureTests
{
    public static TheoryData<string> Fixtures() =>
        [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "KB*" + FactFixture.Suffix).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    /// <summary>A floor, so the theory below cannot pass on a build that copied no fixture.</summary>
    [Fact]
    public void The_fixtures_are_copied_beside_the_tests() => Fixtures().Count.ShouldBeGreaterThanOrEqualTo(4);

    [Theory, MemberData(nameof(Fixtures))]
    public void A_fixture_loads_and_its_fact_has_a_test_here(string file)
    {
        var fixture = FactFixture.Load(Path.Combine(AppContext.BaseDirectory, file));

        fixture.Answers.ShouldNotBeEmpty();
        typeof(FixtureTests).Assembly.GetType($"{typeof(FixtureTests).Namespace}.{fixture.Fact.Replace("-", "", StringComparison.Ordinal)}").ShouldNotBeNull();
    }

    private static FactFixture Write(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fixture-{Guid.NewGuid():N}", "KB099.fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        try { return FactFixture.Load(path); }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private const string Two = """
        { "fact": "KB-099", "answers": [
          { "id": "a", "kind": "document", "when": { "x": "1" }, "at": "/p/~1q/0", "value": { "v": [1, null] }, "note": "the note" },
          { "id": "b", "kind": "status", "when": { "x": "2", "y": "1" }, "value": { "status": 200, "body": "0" } },
          { "id": "c", "kind": "exception", "when": { "x": "2" }, "value": { "type": "System.InvalidOperationException", "message": "depth '1'" } }
        ] }
        """;

    [Fact]
    public void An_answer_covers_a_row_that_holds_every_setting_it_names()
    {
        var fixture = Write(Two);

        fixture.For(new Dictionary<string, string> { ["x"] = "1", ["z"] = "9" })!.Id.ShouldBe("a");
        fixture.For(new Dictionary<string, string> { ["x"] = "3" }).ShouldBeNull("no answer covers it: the harness measures the framework itself");
        Should.Throw<InvalidOperationException>(() => fixture.For(new Dictionary<string, string> { ["x"] = "2", ["y"] = "1" }))
              .Message.ShouldBe("KB-099's fixture is ambiguous for these settings: b, c each cover them");
    }

    [Fact]
    public void Each_kind_compares_what_it_holds()
    {
        var fixture = Write(Two);

        fixture["a"].Mismatch(JsonNode.Parse("""{ "p": { "/q": [{ "v": [1, null] }] } }""")!).ShouldBeNull();
        fixture["a"].Mismatch(JsonNode.Parse("""{ "p": { "/q": [{ "v": [1] }] } }""")!).ShouldBe("""a at /p/~1q/0: measured {"v":[1]}, the fixture says {"v":[1,null]} (the note)""");
        fixture["a"].Mismatch(JsonNode.Parse("{}")!).ShouldBe("""a at /p/~1q/0: measured (nothing), the fixture says {"v":[1,null]} (the note)""");
        fixture["b"].Mismatch(200, "0").ShouldBeNull();
        fixture["b"].Mismatch(400, "0").ShouldBe("b: measured status 400, the fixture says 200");
        fixture["b"].Mismatch(200, "5").ShouldBe("b: measured body 5, the fixture says 0");
        fixture["c"].Mismatch(new InvalidOperationException("exceeded depth '1' at V")).ShouldBeNull();
        fixture["c"].Mismatch(new ArgumentException("depth '1'")).ShouldBe("c: measured System.ArgumentException, the fixture says System.InvalidOperationException");
        fixture["c"].Mismatch((Exception?)null).ShouldStartWith("c: nothing was thrown");
        Should.Throw<InvalidOperationException>(() => fixture["b"].Mismatch(JsonNode.Parse("{}")!));
    }

    [Theory]
    [InlineData("""{ "fact": "KB-098", "answers": [] }""", "KB099.fixture.json names the fact KB-098, whose fixture is KB098.fixture.json")]
    [InlineData("""{ "fact": "KB-099", "answers": [{ "id": "a", "kind": "document", "value": 1 }] }""", "KB099.fixture.json answer a is a document and needs \"at\", a JSON pointer such as /paths/~1a/get")]
    [InlineData("""{ "fact": "KB-099", "answers": [{ "id": "a", "kind": "status", "value": { "status": "200" } }] }""", "KB099.fixture.json answer a is a status and needs a \"status\" number")]
    [InlineData("""{ "fact": "KB-099", "answers": [{ "id": "a", "kind": "exception", "value": {} }] }""", "KB099.fixture.json answer a is an exception and needs its \"type\"")]
    [InlineData("""{ "fact": "KB-099", "answers": [{ "id": "a", "kind": "guess", "value": 1 }] }""", "KB099.fixture.json answer a's kind 'guess' is none of document, status and exception")]
    [InlineData("""{ "fact": "KB-099", "answers": [{ "id": "a", "kind": "status", "value": { "status": 1 } }, { "id": "a", "kind": "status", "value": { "status": 1 } }] }""", "KB099.fixture.json lists the answer a twice")]
    public void A_fixture_that_says_less_than_its_kind_needs_is_refused(string json, string message) =>
        Should.Throw<InvalidDataException>(() => Write(json)).Message.ShouldBe(message);

    /// <summary>The reader is meant to be compiled into the probe harness as it is: nothing of the test framework in it.</summary>
    [Fact]
    public void The_reader_needs_only_the_base_class_library()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CodoMetis.TypeKit.slnx"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("no CodoMetis.TypeKit.slnx above the test output");

        File.ReadAllLines(Path.Combine(root, "test", "CodoMetis.TypeKit.Facts", "FactFixture.cs"))
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal))
            .ShouldAllBe(line => line.StartsWith("using System", StringComparison.Ordinal));
        typeof(FactFixture).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
            .ShouldAllBe(type => type.Namespace!.StartsWith("System", StringComparison.Ordinal) || type.Namespace == typeof(FactFixture).Namespace);
    }
}
