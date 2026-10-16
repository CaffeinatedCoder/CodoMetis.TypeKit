// Claims C03-composed-faults (AspNetCore README#faults-of-several-types) and C05-with-rule (README#the-rule-that-refused).
// C03: a Fault<T1,T2[,T3]> is "answered as its own set maps it" (status, `fault`, problem type, detail, rule intact); "its operation lists the
//   statuses of both sets, in ascending order, with the problem types of both where they share a status"; "the host doesn't start while either
//   set is unmapped". The host maps every fault set with a problem type of its set and kind (TypeOf), so two sets sharing a status name two types.
// C05: WithRule(member) is written as `rule`, "qualified with its type's name", a nested type after its nesting chain "whatever QualifiedFaultNames
//   says"; "The status, the mapping and the document stay the error's"; a value no member names "throws where it's given"; the document describes `rule`.
// Oracle: claim (README text) + differential wire/document. Expected: see Cmp.Expect / Cmp.DocStatuses. Each obligation is a check of its
// own (ComposedChecks.C03, C05), every check on one host.
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Probes.Faults;
using Probes.Shapes;
namespace Probes;

public static class ComposedClaims
{
    /// <summary>The application's own string converter (json.Converters=own-for-wrapped) wraps every string it writes: take it off again.</summary>
    static string? Un(Row r, string? s) => r.Is("json.Converters", "own-for-wrapped") && s is not null && s.StartsWith("own:") ? s[4..].Trim('"') : s;
    static string Cut(string? s) => s is null ? "(null)" : s.Length > 200 ? s[..200] + "..." : s;
    public static string Variant(Row r) => r["http.response"] switch
    {
        "endpoint-without-faults" => "wo", "group-with-fault-status" => "group", "declared-produces-problem" => "declared", "two-faults-same-name" => "dup", _ => "plain",
    };
    static HostCfg Cfg(Row r) => new()
    {
        FaultControllers = true, Controllers = Hx.Plain(r) ? [typeof(RulePlainController)] : [typeof(RuleApiController)],
        Minimal = Cmp.MapMinimal, Qualified = r.Is("qualified", "QualifiedFaultNames"),
        Mapping = "own-per-set-type",
        Services = s => s.AddFaultResponses(f => FaultResponse.For(f.Kind).WithType(new Uri(TypeOf(f.GetType().Name, f.Kind.ToString())))),
    };

    /// <summary>The problem type the host maps a fault-set member to: its set's and its kind's, so two sets sharing a status name two types.</summary>
    public static string TypeOf(string set, string kind) => $"https://p.example/{set}/{kind}";

    /// <summary>The problem's rule, an aspect of its own beside <see cref="Aspect"/>'s.</summary>
    public const string Rule = "rule";

    /// <summary>
    /// The wire and the document of one endpoint family under the row, one check (Row.Check) per obligation <paramref name="of"/> names an
    /// aspect for (<see cref="Aspect"/>'s and <see cref="Rule"/>), against <paramref name="against"/>; every check runs on the one host, with
    /// the answers fetched once. e: the error code of Cmp.
    /// </summary>
    public static async Task<string?> WireAndDoc(Row r, Entry en, int arity, int e, string rule, Func<string, string[]> of, Baseline against)
    {
        var variant = Variant(r);
        var obligations = Aspect.Answer.Append(Rule).Concat(Aspect.Doc).SelectMany(of).Distinct().ToList();
        WebApplication? app = null; Exception? down = null;
        try { app = await Hx.Start(r, en, Cfg(r)); }
        catch (Exception ex) { down = ex; }
        await using var _ = app;
        string? failed = null;
        if (down is not null)
        {
            foreach (var o in obligations) { var f = r.Check(o, against, _ => "host did not start: " + down.GetType().Name + ": " + Cut(down.Message)); failed ??= f; }
            return failed;
        }
        var qualified = r.Is("qualified", "QualifiedFaultNames");
        var sides = new List<string>();
        if (Hx.HasMinimal(r)) sides.Add(Hx.M(r, $"/m/{arity}/{variant}"));
        if (Hx.HasMvc(r)) sides.Add(Hx.C(r, $"/c/{arity}/{variant}"));
        JsonNode? doc = null; Exception? docFailed = null;
        try { doc = await Hx.Doc(app!); }
        catch (Exception ex) { docFailed = ex; }
        var wires = new Dictionary<string, Hx.Wire>();
        foreach (var path in sides) wires[path] = await Hx.Get(app!, $"{path}?e={e}");
        var (type, member, status) = Cmp.Expect(arity, variant, e);

        foreach (var o in obligations)
        {
            var f = await r.Check(o, against, async c =>
            {
                bool want(string aspect) => of(aspect).Contains(o);
                if (Aspect.Answer.Any(want) || want(Rule))
                    foreach (var path in sides)
                    {
                        var w = wires[path];
                        if (w.Thrown is not null) return $"{path} e={e}: request threw {w.Thrown}";
                        if (want(Aspect.Status) && (int)w.Status != status) return $"{path} e={e}: status {(int)w.Status}, expected {status}: {Cut(w.Body)}";
                        JsonNode? body; try { body = JsonNode.Parse(w.Body); } catch { return $"{path} e={e}: body is not JSON: {Cut(w.Body)}"; }
                        var fault = Un(r, body?["fault"]?.GetValue<string>());
                        var wantFault = qualified ? $"{type}.{member}" : member;
                        if (want(Aspect.Fault) && fault != wantFault) return $"{path} e={e}: fault {fault}, expected {wantFault}";
                        if (want(Aspect.Detail) && e == 3 && Un(r, body?["detail"]?.GetValue<string>()) != Cmp.Detail) return $"{path} e={e}: detail {body?["detail"]}, expected the occurrence's";
                        var gotRule = Un(r, body?["rule"]?.GetValue<string>());
                        if (want(Rule) && gotRule != (rule == "" ? null : rule)) return $"{path} e={e}: rule {gotRule ?? "(none)"}, expected {(rule == "" ? "(none)" : rule)}";
                        // an enum (SFault) is mapped to a status alone, and ASP.NET writes its own default type there
                        if (want(Aspect.Type) && Cmp.KindOf(type, member) is { } kind && Un(r, body?["type"]?.GetValue<string>()) is var gotType && gotType != TypeOf(type, kind))
                            return $"{path} e={e}: type {gotType ?? "(none)"}, expected {TypeOf(type, kind)}";
                        if (want(Aspect.Status) && int.Parse(body?["status"]?.ToString() is { } st ? Un(r, st)!.Trim('"') : "0") != status) return $"{path} e={e}: problem status field {body?["status"]}";
                    }
                if (!Aspect.Doc.Any(want)) return null;
                // the document: one ASP.NET alone refuses the same way has nothing to check (noted there)
                if (docFailed is not null)
                    return await Hx.DocRefusedByAspNetAlone(r, en, Cfg(r), docFailed) is { } alone
                        ? alone + " @ " + string.Join(" < ", (docFailed.StackTrace ?? "").Split('\n').Take(4).Select(x => x.Trim().Split('(')[0]))
                        : null;
                foreach (var path in sides)
                {
                    var ops = doc!["paths"]?[path]?["get"];
                    if (ops is null && !Hx.InDocument(r) && path.Contains("/c/")) { c.Note(NoteKeys.DocControllerNotInApiExplorer); continue; }
                    if (ops is null) return $"document has no GET {path}";
                    var keys = ops["responses"]!.AsObject().Select(kv => kv.Key).Where(k => int.TryParse(k, out var n) && n >= 400).Select(int.Parse).ToList();
                    var listed = Cmp.DocStatuses(arity, variant);
                    // A status the endpoint declares itself (the 429) takes its place among the sets' statuses (fixed in 8d1ebd8).
                    if ((want(Aspect.DocListed) || want(Aspect.DocOrder)) && !keys.SequenceEqual(listed)) return $"{path}: document lists {string.Join(",", keys)}, expected {string.Join(",", listed)} (ascending)";
                    foreach (var k in keys)
                    {
                        if (variant == "declared" && k == 429) continue;   // a declared problem is ASP.NET's, not an answered fault
                        var txt = Hx.Deep(doc!, ops["responses"]![k.ToString()])?.ToJsonString() ?? "";
                        if (want(Aspect.DocDescribes) && !txt.Contains("\"fault\"")) return $"{path} {k}: response schema does not describe fault: {Cut(txt)}";
                        if (want(Aspect.DocDescribes) && !txt.Contains("\"rule\"")) return $"{path} {k}: response schema does not describe rule: {Cut(txt)}";
                        var desc = ops["responses"]![k.ToString()]?["description"]?.GetValue<string>() ?? "";
                        const string Lead = "Problem types: ";
                        var at = desc.IndexOf(Lead, StringComparison.Ordinal);
                        var types = at < 0 ? [] : desc[(at + Lead.Length)..].Split(", ").ToHashSet();
                        var wantTypes = Cmp.TypesAt(arity, variant, k);
                        if (want(Aspect.DocTypes) && !types.SetEquals(wantTypes))
                            return $"{path} {k}: description \"{desc}\" names problem types {{{string.Join(", ", types)}}}, expected {{{string.Join(", ", wantTypes)}}}";
                    }
                }
                return null;
            });
            failed ??= f;
        }
        return failed;
    }

    /// <summary>"The host doesn't start while either set is unmapped."</summary>
    public static async Task<string?> UnmappedDoesNotStart(Row r, Entry en, int arity, int e)
    {
        var cfg = Cfg(r); cfg.MapBlanketMapping = false;
        var unmapped = e == 1 ? "HFault" : arity == 2 ? "GFault" : "GFault";
        var variant = Variant(r);
        if (variant == "dup") unmapped = e == 1 ? "HFault" : "KFault";
        cfg.Services = s =>
        {
            if (unmapped != "HFault") s.AddFaultResponses<HFault>();
            if (unmapped != "GFault" && variant != "dup") s.AddFaultResponses<GFault>();
            if (unmapped != "KFault" && variant == "dup") s.AddFaultResponses<KFault>();
            if (arity == 3) s.AddFaultResponses<SFault>(f => f switch { SFault.First => 409, _ => 410 });
        };
        WebApplication? app = null;
        try { app = await Hx.Start(r, en, cfg); }
        catch (Exception ex)
        {
            return ex.ToString().Contains(unmapped) ? null : $"host refused to start but not naming {unmapped}: {ex.GetType().Name}: {Cut(ex.Message)}";
        }
        await using var _ = app;
        return $"host started with {unmapped} unmapped (arity {arity}, variant {variant}); answered only at request time?";
    }
}

public static class ComposedChecks
{
    static void Process(Row r, Entry en) { if (r.Is("process.converter-lookup", "wrapped-type-converter-looked-up-first")) _ = System.ComponentModel.TypeDescriptor.GetConverter(Make.Held(en.Plain)); }
    /// <summary>
    /// C03, a check per obligation: each-error-own-set (status, fault, problem type, and the rule where the error names none),
    /// detail-and-rule-intact (error with-detail or with-rule: the detail and the rule), statuses-ascending (the statuses each operation
    /// lists, in order, and their problem schemas), shared-status-types (the problem types each status's description names), and
    /// unmapped-set-stops-start (a host with one set unmapped, against the refusal) in every row.
    /// </summary>
    public static async Task<string?> C03(Row r)
    {
        var en = Registry.Of(r); Process(r, en);
        var arity = int.Parse(r["arity"]);
        var e = r["error"] switch { "first-set" => 1, "last-set" => 2, "with-detail" => 3, _ => 4 };
        var intact = r["error"] is "with-detail" or "with-rule";
        string[] Of(string aspect) => aspect switch
        {
            Aspect.Detail or ComposedClaims.Rule when intact => ["detail-and-rule-intact"],
            Aspect.DocListed or Aspect.DocOrder or Aspect.DocDescribes => ["statuses-ascending"],
            Aspect.DocTypes => ["shared-status-types"],
            _ => ["each-error-own-set"],
        };
        var answered = await ComposedClaims.WireAndDoc(r, en, arity, e, e == 4 ? "PRule.A" : "", Of,
            Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#faults-of-several-types"));
        var unmapped = await r.Check("unmapped-set-stops-start", Baseline.Refusal, _ => ComposedClaims.UnmappedDoesNotStart(r, en, arity, e));
        return answered ?? unmapped;
    }
    /// <summary>
    /// C05, a check per obligation: unnamed-value-throws (ruletype flags-combination or undefined-cast, against the refusal);
    /// rule-qualified (enum, closed-set) or nested-chain (nested-enum), and no-namespace, for the rule on the wire; stays-the-errors for
    /// the status, fault, problem type and the document, and in the rows of an unnamed value for the error answered without a rule.
    /// </summary>
    public static async Task<string?> C05(Row r)
    {
        var en = Registry.Of(r); Process(r, en);
        var ruletype = r["ruletype"];
        // a value no member names throws where it is given (never a rule on the wire)
        var thrown = ruletype switch
        {
            "flags-combination" => r.Check("unnamed-value-throws", Baseline.Refusal, c =>
            {
                try { _ = Fault.Of(HFault.Clash).WithRule((FRule)3); return "WithRule(flags combination) did not throw"; } catch (ArgumentException) { return null; }
            }),
            "undefined-cast" => r.Check("unnamed-value-throws", Baseline.Refusal, c =>
            {
                try { _ = Fault.Of(HFault.Clash).WithRule((PRule)9); return "WithRule(undefined cast) did not throw"; } catch (ArgumentException) { return null; }
            }),
            _ => null,
        };
        var (e, rule) = ruletype switch { "enum" => (4, "PRule.A"), "closed-set" => (5, "RuleSet.X"), "nested-enum" => (6, "Holder.NestedRule.N"), _ => (1, "") };
        var named = ruletype switch { "enum" or "closed-set" => "rule-qualified", "nested-enum" => "nested-chain", _ => null };
        string[] Of(string aspect) => aspect == ComposedClaims.Rule && named is not null ? [named, "no-namespace"] : ["stays-the-errors"];
        var answered = await ComposedClaims.WireAndDoc(r, en, 3, e, rule, Of, Baseline.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#the-rule-that-refused"));
        return thrown ?? answered;
    }
}
public static class ComposedShrinkInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Init() { ShrinkChecks.All["C03"] = ComposedChecks.C03; ShrinkChecks.All["C05"] = ComposedChecks.C05; }
}
public sealed class C03ComposedProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C03ComposedProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C03", idx, ComposedChecks.C03, probe);
}
public sealed class C05RuleProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C05RuleProbe");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string probe, int idx) => Rec.Run("C05", idx, ComposedChecks.C05, probe);
}
