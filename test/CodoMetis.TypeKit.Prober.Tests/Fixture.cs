using System.Diagnostics;
using System.Text.Json.Nodes;
using Prober;

namespace CodoMetis.TypeKit.Prober.Tests;

/// <summary>A repository with one claim C1 over axes x and y (values 1 and 2) and one probe P covering it.</summary>
internal sealed class Fixture : IDisposable
{
    public Fixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "prober-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Memory = Path.Combine(Root, ".claude", "agent-memory-local", "prober");
        BranchDir = Path.Combine(Memory, "branches", "main");
        Sh("git", "init", "-q", "-b", "main");
        Write("README.md", "# a\n\nThe claim.\n");
        Write("src/a.cs", "class A {}\n");
        Write(".claude/probe-axes.md", "```json probe-axes\n" + new JsonObject
        {
            ["axes"] = new JsonObject { ["x"] = new JsonArray("1", "2"), ["y"] = new JsonArray("1", "2") },
            ["surfaces"] = new JsonObject { ["s"] = new JsonObject { ["axes"] = new JsonArray("x", "y"), ["watched"] = new JsonArray("src") } },
        }.ToJsonString() + "\n```\n");
        Write("tools/probe/corpus/claims.json", """[{"id": "C1", "source": "README.md#a", "surfaces": ["s"]}]""");
        Write("tools/probe/corpus/registry.json", """{"probes": {"P": {"claim": "C1", "filter": "*P"}}}""");
        Write("tools/probe/corpus/harness/P.cs", "// probe P\n");
        Rows([("1", "1"), ("2", "2")]);
        Commit("init");
        Directory.CreateDirectory(BranchDir);
        File.WriteAllText(Path.Combine(Memory, "axes-generated.json"), """{"axes": {}, "needsValues": []}""");
        Ledger("universe", "--base", "HEAD").Exit.ShouldBe(0);
    }

    public string Root { get; }
    public string Memory { get; }
    public string BranchDir { get; }

    public void Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Rows(IEnumerable<(string X, string Y)> rows) =>
        Write("tools/probe/corpus/rows/C1.json", new JsonObject
        {
            ["claim"] = "C1",
            ["rows"] = new JsonArray([.. rows.Select(r => (JsonNode)new JsonObject { ["row"] = new JsonObject { ["x"] = r.X, ["y"] = r.Y } })]),
        }.ToJsonString());

    public void Commit(string message)
    {
        Sh("git", "add", "-A");
        Sh("git", "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", message);
    }

    public string Sh(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEnd(); var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.ShouldBe(0, $"{file} {string.Join(' ', args)}: {output}{error}");
        return output;
    }

    /// <summary>Runs a prober command in-process, in this repository, with the environment given.</summary>
    public (int Exit, string Out, string Err) Ledger(params string[] args) => LedgerWith(new Dictionary<string, string?>(), args);

    public (int Exit, string Out, string Err) LedgerWith(Dictionary<string, string?> env, params string[] args)
    {
        var output = new StringWriter(); var error = new StringWriter();
        var exit = Program.Run(new Context(Root, env, output, error), args);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>Runs the built dll as its own process: for the tests that race several takers.</summary>
    public Process Spawn(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var a in args) start.ArgumentList.Add(a);
        return Process.Start(start)!;
    }

    public void Footprint()
    {
        var blob = Sh("git", "rev-parse", "HEAD:src/a.cs").Trim();
        File.AppendAllText(Path.Combine(BranchDir, "footprints.jsonl"),
            new JsonObject { ["probe"] = "P", ["footprint"] = new JsonObject { ["src/a.cs"] = blob }, ["compileTime"] = new JsonObject(), ["toolchain"] = new JsonObject() }.ToJsonString() + "\n");
    }

    /// <summary>What a replay of P records: the rows it ran, how each ended and the settings that applied.</summary>
    public string Evidence(IReadOnlyList<Dictionary<string, string>> applied, string category = "pass", int[]? executed = null)
    {
        var idx = Enumerable.Range(0, applied.Count).ToArray();
        var path = Path.Combine(Root, "evidence.json");
        File.WriteAllText(path, new JsonObject
        {
            ["variants"] = new JsonArray(new JsonObject
            {
                ["env"] = new JsonObject(), ["expected"] = new JsonArray([.. idx.Select(i => (JsonNode)i)]),
                ["executed"] = new JsonArray([.. (executed ?? idx).Select(i => (JsonNode)i)]),
            }),
            ["rows"] = new JsonObject(idx.Select(i => KeyValuePair.Create(i.ToString(System.Globalization.CultureInfo.InvariantCulture), (JsonNode?)new JsonObject
            {
                ["category"] = category, ["applied"] = new JsonObject(applied[i].Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
            }))),
        }.ToJsonString());
        return path;
    }

    public int Covered()
    {
        var (exit, output, error) = Ledger("status", "--json");
        exit.ShouldBe(0, error);
        return (int)JsonNode.Parse(output)!["claims"]!["C1"]!["current"]!.GetValue<double>();
    }

    public void AgeTheLock()
    {
        var path = Path.Combine(Memory, "round.lock");
        var held = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        held["heartbeat"] = held["heartbeat"]!.GetValue<double>() - 21 * 60;
        File.WriteAllText(path, held.ToJsonString());
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
