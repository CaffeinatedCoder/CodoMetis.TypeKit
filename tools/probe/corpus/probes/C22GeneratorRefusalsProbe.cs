// Claim C22-generator-refusals (src/CodoMetis.TypeKit.Generators/README.md#build-errors), surfaces generation, analyzers, family build.
// Obligations (claims.json), each with a check below and a seed of its own in the registry entry (decision 104):
//   no-type-half-generated: "A declaration that can't be generated fails the build, so no type is left half-generated."
//   every-error-links-the-table: "Every error ends with a link to this table."
//   unsealed-record-class: "A record class not declared `sealed`."
//   over-value-object: "or a value object (itself included). Wrap what the other value object wraps instead."
//   generic-wrapped: "A generic underlying type, a tuple included, fails the build (CMTK1005)."
//   abstract: "An abstract type with a value-object marker. It would get no member, and a value held as it, of a type derived from it, would have no `Value` and be written as `{}`."
//   over-object: "or wrapping `object`, `System.Enum`, an array, a pointer, a nullable type, a generic type (a tuple included) or a value object (itself included)."
//   over-system-enum: "or wrapping `object`, `System.Enum`, an array, a pointer, a nullable type, a generic type (a tuple included) or a value object (itself included)."
//   named-as-generated-member: "named `Value` or as another member the generators add (`From`, `TryFrom`, `FromKnownGood`, `Revalidate`, `Parse`, `TryParse`, `TryFormat`, `MinValue`, `MaxValue`, `CompareTo`)"
//   closed-set-named-as-generated-member: "a set itself named as one of them (a member can't have the name of its type)"
//   equality-operator: "So a hand-written `Equals(TSelf)`, `GetHashCode()`, `IEquatable<TSelf>.Equals`, or `operator ==` or `!=` between two of the type, fails the build (CMTK1011)."
// Oracle: the refusal (Baseline.Refusal). Each row's file of a consumer build (harness/BuildRefusals.cs: the SDK build of a throwaway
// project in a child process, as C18 and BuildOutcomeTests build, since Metalama runs only there) declares the row's shape.refused-kind as
// a value object or a closed set of shape.kind at shape.site, over shape.inheritance and shape.wrapped, and beside it a control: the same
// declaration without what is refused. Three consumers, since a compiler error in one file hid the generators' errors in the others
// (e4b751c): the value objects, whose files hold no compiler error; the closed sets, whose refusal leaves IClosedSet's members
// unimplemented (CS0535, as BuildOutcomeTests allows); and the equality operators, which C# itself reports as CS0111. Per row:
//   the refusal's own obligation: an error of the README's id that names the type, and beside it no Metalama error (an aspect that
//   failed, or generated code that does not compile) and no compiler error but the documented one;
//   no-type-half-generated: the build failed, the type's file reports an error, and in Metalama's transformed source the type is as it
//   was written: no base type, no member and no {Name}Extensions companion added;
//   every-error-links-the-table: every CMTK1xxx error of the file ends with ". See <the README's #build-errors>", and the README has
//   the heading.
// A refused kind the harness does not declare, one the README's table no longer lists, and a control the generators refuse or do not
// generate are harness errors: the row would show nothing about a refusal the README states.
using Probes.Build;
using Probes.Shapes;
namespace Probes;
public sealed class C22GeneratorRefusalsProbe
{
    const string RowsFile = "C22GeneratorRefusalsProbe"; // the probe's own rows

    public static TheoryData<string, int> Data() => Rows.Data(RowsFile);
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run(probe, idx, Check);

    static Task<RefusalOutcome> Built(string group) => Refusals.Build($"{RowsFile}/{group}", () =>
        Rows.Load(RowsFile).Select(Refusals.Declare).Where(d => d.Refusal.Group == group).ToDictionary(d => d.File, d => d.Source));

    static async Task<string?> Check(Row r)
    {
        var d = Refusals.Declare(r);
        var build = await Built(d.Refusal.Group);
        var mine = build.In(d.File).ToList();

        // What would make the row show nothing: no transformed source, a compiler error elsewhere that hides this file's, a control
        // that is refused or not generated.
        if (build.Transformed.Count == 0)
        { r.Outcome("harness-error", $"the consumer wrote no transformed source (exit {build.ExitCode}): " + Errors(build.Diagnostics.Where(x => x.Severity == "error").Take(3))); return null; }
        var hiding = build.Diagnostics.Where(x => x.Severity == "error" && x.Id.StartsWith("CS", StringComparison.Ordinal)
                                                 && !d.Refusal.Companions.Contains(x.Id) && Path.GetFileName(x.File) != d.File).ToList();
        if (hiding.Count > 0)
        { r.Outcome("harness-error", "a compiler error in another file hides the generators' errors in this one (e4b751c): " + Errors(hiding.Take(3))); return null; }
        var control = d.Display[..^d.Name.Length] + d.Control;
        if (mine.Where(x => x.Severity == "error" && x.Message.Contains($"'{control}'", StringComparison.Ordinal)).ToList() is { Count: > 0 } refusedControl)
        { r.Outcome("harness-error", $"the control {control} is refused, so the row shows nothing about {d.Refusal.Kind}: " + Errors(refusedControl)); return null; }
        if (Refusals.Added(build, d, d.Control) is null)
        { r.Outcome("harness-error", $"the control {control} is not generated, so the row shows nothing about {d.Refusal.Kind}"); return null; }

        // Every check runs, so a seed that breaks one obligation is seen by its own check whatever the others find.
        var named = r.Check(d.Refusal.Obligation, Baseline.Refusal, c => RefusedByName(c, d, mine));
        var whole = r.Check("no-type-half-generated", Baseline.Refusal, c => NotHalfGenerated(c, d, build, mine));
        var linked = r.Check("every-error-links-the-table", Baseline.Docs("src/CodoMetis.TypeKit.Generators/README.md#build-errors"), c => Linked(c, mine));
        return named ?? whole ?? linked;
    }

    static string Errors(IEnumerable<BuildDiagnostic> errors) => string.Join("; ", errors.Select(e => $"{Path.GetFileName(e.File)} {e.Id} {e.Message}"));

    static string? RefusedByName(Probes.Shapes.Check c, RefusedDeclaration d, List<BuildDiagnostic> mine)
    {
        var errors = mine.Where(e => e.Severity == "error").ToList();
        if (!errors.Any(e => e.Id == d.Refusal.Id && e.Message.StartsWith($"'{d.Display}'", StringComparison.Ordinal)))
            return $"{d.Display} ({d.Refusal.Kind}) is not refused with {d.Refusal.Id} by name: " + (errors.Count == 0 ? "its file reports no error" : Errors(errors));
        if (errors.Where(e => e.Id.StartsWith("LAMA", StringComparison.Ordinal)).ToList() is { Count: > 0 } aspect)
            return $"beside {d.Refusal.Id}, an aspect failed on the declaration or generated code that does not compile: " + Errors(aspect);
        if (errors.Where(e => e.Id.StartsWith("CS", StringComparison.Ordinal) && !d.Refusal.Companions.Contains(e.Id)).ToList() is { Count: > 0 } compiler)
            return $"beside {d.Refusal.Id}, compiler errors the README does not name: " + Errors(compiler);
        c.Note(NoteKeys.Compared, $"{d.Refusal.Id} names {d.Display}");
        return null;
    }

    static string? NotHalfGenerated(Probes.Shapes.Check c, RefusedDeclaration d, RefusalOutcome build, List<BuildDiagnostic> mine)
    {
        if (build.ExitCode == 0) return $"the consumer with {d.Display} ({d.Refusal.Kind}) in it built";
        if (!mine.Any(e => e.Severity == "error")) return $"{d.Display} ({d.Refusal.Kind}) compiles: its file reports no error";
        if (Refusals.Added(build, d, d.Name) is { } added) return $"{d.Display} ({d.Refusal.Kind}) is half-generated: the generators added {added}";
        c.Note(NoteKeys.Compared, $"the build failed and {d.Display} is as written");
        return null;
    }

    static string? Linked(Probes.Shapes.Check c, List<BuildDiagnostic> mine)
    {
        if (!Refusals.ReadmeHasBuildErrorsHeading()) return "the Generators README has no '## Build errors' heading, which the errors link to";
        var generator = mine.Where(e => e.Severity == "error" && e.Id.StartsWith("CMTK1", StringComparison.Ordinal)).ToList();
        if (generator.Count == 0) return c.Inconclusive("no generator error in the row's file to read");
        if (generator.Where(e => !e.Message.EndsWith($". See {Refusals.TableLink}", StringComparison.Ordinal)).ToList() is { Count: > 0 } bare)
            return "these errors do not end with the link to the Build errors table: " + Errors(bare);
        c.Note(NoteKeys.Compared, $"{generator.Count} error(s) end with the link");
        return null;
    }
}
