// Claims C03-composed-faults (AspNetCore README#faults-of-several-types) and C05-with-rule (README#the-rule-that-refused).
// C03: a Fault<T1,T2[,T3]> is "answered as its own set maps it" (status, `fault`, detail, rule intact); "its operation lists the statuses of both
//   sets, in ascending order"; "the host doesn't start while either set is unmapped".
// C05: WithRule(member) is written as `rule`, "qualified with its type's name", a nested type after its nesting chain "whatever QualifiedFaultNames
//   says"; "The status, the mapping and the document stay the error's"; a value no member names "throws where it's given"; the document describes `rule`.
// Oracle: claim (README text) + differential wire/document. Expected: see Cmp.Expect / Cmp.DocStatuses.
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
    };

    /// <summary>The wire and the document of one endpoint family under the row. e: the error code of Cmp.</summary>
    public static async Task<string?> WireAndDoc(Row r, Entry en, int arity, int e, string rule)
    {
        var variant = Variant(r);
        WebApplication app;
        try { app = await Hx.Start(r, en, Cfg(r)); }
        catch (Exception ex) { return "host did not start: " + ex.GetType().Name + ": " + Cut(ex.Message); }
        await using var _ = app;
        var qualified = r.Is("qualified", "QualifiedFaultNames");
        var sides = new List<string>();
        if (Hx.HasMinimal(r)) sides.Add(Hx.M(r, $"/m/{arity}/{variant}"));
        if (Hx.HasMvc(r)) sides.Add(Hx.C(r, $"/c/{arity}/{variant}"));
        JsonNode? doc = null; string? docFail = null;
        try { doc = await Hx.Doc(app); }
        catch (Exception ex)
        {
            var alone = await Hx.DocRefusedByAspNetAlone(r, en, Cfg(r), ex);
            if (alone is not null) docFail = alone + " @ " + string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(x => x.Trim().Split('(')[0]));
        }
        foreach (var path in sides)
        {
            var w = await Hx.Get(app, $"{path}?e={e}");
            var (type, member, status) = Cmp.Expect(arity, variant, e);
            if (w.Thrown is not null) return $"{path} e={e}: request threw {w.Thrown}";
            if ((int)w.Status != status) return $"{path} e={e}: status {(int)w.Status}, expected {status}: {Cut(w.Body)}";
            JsonNode? body; try { body = JsonNode.Parse(w.Body); } catch { return $"{path} e={e}: body is not JSON: {Cut(w.Body)}"; }
            var fault = Un(r, body?["fault"]?.GetValue<string>());
            var wantFault = qualified ? $"{type}.{member}" : member;
            if (fault != wantFault) return $"{path} e={e}: fault {fault}, expected {wantFault}";
            if (e == 3 && Un(r, body?["detail"]?.GetValue<string>()) != Cmp.Detail) return $"{path} e={e}: detail {body?["detail"]}, expected the occurrence's";
            var gotRule = Un(r, body?["rule"]?.GetValue<string>());
            if (gotRule != (rule == "" ? null : rule)) return $"{path} e={e}: rule {gotRule ?? "(none)"}, expected {(rule == "" ? "(none)" : rule)}";
            if (int.Parse(body?["status"]?.ToString() is { } st ? Un(r, st)!.Trim('"') : "0") != status) return $"{path} e={e}: problem status field {body?["status"]}";
            // the document
            if (docFail is not null) return docFail;
            if (doc is null) continue;   // ASP.NET alone refuses this document too (noted)
            var ops = doc["paths"]?[path]?["get"];
            if (ops is null && !Hx.InDocument(r) && path.Contains("/c/")) { r.Note(NoteKeys.DocControllerNotInApiExplorer); continue; }
            if (ops is null) return $"document has no GET {path}";
            var keys = ops["responses"]!.AsObject().Select(kv => kv.Key).Where(k => int.TryParse(k, out var n) && n >= 400).Select(int.Parse).ToList();
            var want = Cmp.DocStatuses(arity, variant);
            // A status the endpoint declares itself (the 429) takes its place among the sets' statuses (fixed in 8d1ebd8).
            if (!keys.SequenceEqual(want)) return $"{path}: document lists {string.Join(",", keys)}, expected {string.Join(",", want)} (ascending)";
            foreach (var k in keys)
            {
                if (variant == "declared" && k == 429) continue;   // a declared problem is ASP.NET's, not an answered fault
                var txt = Hx.Deep(doc!, ops["responses"]![k.ToString()])?.ToJsonString() ?? "";
                if (!txt.Contains("\"fault\"")) return $"{path} {k}: response schema does not describe fault: {Cut(txt)}";
                if (!txt.Contains("\"rule\"")) return $"{path} {k}: response schema does not describe rule: {Cut(txt)}";
            }
        }
        return null;
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
    public static async Task<string?> C03(Row r)
    {
        var en = Registry.Of(r); Process(r, en);
        var arity = int.Parse(r["arity"]);
        var e = r["error"] switch { "first-set" => 1, "last-set" => 2, "with-detail" => 3, _ => 4 };
        return await ComposedClaims.WireAndDoc(r, en, arity, e, e == 4 ? "PRule.A" : "") ?? await ComposedClaims.UnmappedDoesNotStart(r, en, arity, e);
    }
    public static async Task<string?> C05(Row r)
    {
        var en = Registry.Of(r); Process(r, en);
        // a value no member names throws where it is given (never a rule on the wire)
        switch (r["ruletype"])
        {
            case "flags-combination":
                try { _ = Fault.Of(HFault.Clash).WithRule((FRule)3); return "WithRule(flags combination) did not throw"; } catch (ArgumentException) { break; }
            case "undefined-cast":
                try { _ = Fault.Of(HFault.Clash).WithRule((PRule)9); return "WithRule(undefined cast) did not throw"; } catch (ArgumentException) { break; }
        }
        var (e, rule) = r["ruletype"] switch { "enum" => (4, "PRule.A"), "closed-set" => (5, "RuleSet.X"), "nested-enum" => (6, "Holder.NestedRule.N"), _ => (1, "") };
        return await ComposedClaims.WireAndDoc(r, en, 3, e, rule);
    }
}
public static class ComposedShrinkInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Init() { ShrinkChecks.All["C03"] = ComposedChecks.C03; ShrinkChecks.All["C05"] = ComposedChecks.C05; }
}
public sealed class C03ComposedProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C03-composed-faults");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run("C03", idx, ComposedChecks.C03, claim);
}
public sealed class C05RuleProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C05-with-rule");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run("C05", idx, ComposedChecks.C05, claim);
}
