// Claim C01-decimal-spelling (docs/json.md#decimals-in-one-spelling): DecimalNumberJsonConverter writes the smallest scale as a
// number, DecimalStringJsonConverter as a string; "Register either one once, and it covers every decimal: nullable ones, dictionary
// keys, and every value object over a decimal, since its JSON is the underlying type's." "Both read a number, or a string holding one,
// and keep the scale they read."
// Oracle: claim (the minimal spelling, computed here independently of the product) plus differential (the value object's document is the
// plain decimal's under the same options), with the options' own leaving-out shown by a baseline without the converter in the same row.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using Probes.Shapes;
namespace Probes.Json;

public static class DecimalSpelling
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const char Bs = (char)92;

    /// <summary>The input axis as decimal text: max28digits is 7.8 at scale 28, the largest scale a decimal has.</summary>
    public static string Text(string input) => input == "max28digits" ? "7.8000000000000000000000000000" : input;

    /// <summary>The minimal spelling, worked out without the product: invariant digits, trailing fractional zeros dropped, a zero written 0.</summary>
    public static string Canonical(decimal d)
    {
        if (d == 0m) return "0";
        var s = d.ToString(Inv);
        return s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;
    }

    static bool IsDecimal(JTok t, decimal d) => t.Type switch
    {
        JsonTokenType.Number => decimal.TryParse(t.Raw, NumberStyles.Float, Inv, out var x) && x == d,
        JsonTokenType.String or JsonTokenType.PropertyName => t.Text is { Length: > 0 } s && s.Trim() == s && decimal.TryParse(s, NumberStyles.Float, Inv, out var y) && y == d,
        _ => false,
    };

    /// <summary>The decimals a read value holds, as the serializer writes them by default: with the scale they hold.</summary>
    static List<decimal> Held(object? v, Type declared, decimal d)
    {
        if (v is null) return [];
        var json = JsonSerializer.Serialize(v, declared, Jx.Plain);
        return Jx.Tokens(json).Where(t => IsDecimal(t, d)).Select(t => decimal.Parse(t.Type == JsonTokenType.Number ? t.Raw : t.Text!, NumberStyles.Float, Inv)).ToList();
    }
    static bool Same(decimal a, decimal b) => decimal.GetBits(a).SequenceEqual(decimal.GetBits(b));
    static string Show(List<decimal> l) => string.Join(",", l.Select(x => x.ToString(Inv)));

    /// <summary>What one form of the document read as: the decimal's and the value object's refusals and the decimals each holds.</summary>
    sealed record Form(string Name, string Json, Exception? Ep, Exception? Ev, List<decimal> Cp, List<decimal> Cv, bool BothRefused);

    /// <summary>
    /// C01's checks, one per obligation, over one write and one read of the row's shape: the converter's spelling of each value
    /// (smallest-scale; number-is-a-number or string-is-a-string, by the row's converter) against the docs' minimal spelling worked out
    /// here; every decimal of the document, a key's included, and the value object's document against the plain decimal's
    /// (covers-every-decimal, the twin); each form of the input read (reads-a-number-or-a-string) with the scale it carries
    /// (keeps-the-scale-it-reads). The options' own leaving-out is shown by a baseline without the converter in the same row: a
    /// direction it leaves out has nothing to check. Every check runs; returns the first failure.
    /// </summary>
    public static string? Check(Row r)
    {
        var kind = r["shape.kind"];
        if (kind == "closed-set") { r.Outcome("harness-error", "C01 excludes closed-set: a closed set holds no decimal"); return null; }
        var e = Registry.Get(kind, "decimal");
        var text = Text(r["input"]);
        var d = decimal.Parse(text, NumberStyles.Float, Inv);
        var canon = Canonical(d);
        var number = r["converter"] == "number";
        JsonConverter conv = number ? new DecimalNumberJsonConverter() : new DecimalStringJsonConverter();
        var position = r["shape.position"];
        var containing = position == "root" ? "class" : r["shape.containing"];
        var (hp, hv) = Sides.Twins(position, containing, e, d, Make.Vo(e, d));
        var o = JCtx.Apply(r, e, hp.Declared, hv.Declared);
        var ob = new JsonSerializerOptions(o);      // the baseline: the same options, the serializer's own decimal
        foreach (var c in ob.Converters.Where(c => c.CanConvert(typeof(decimal))).ToList()) ob.Converters.Remove(c);
        o.Converters.Insert(0, conv);               // registered first: an application converter for decimals (own-for-wrapped) comes after it
        var ownVo = r.Is("json.Converters", "own-for-value-object");
        var docs = Baseline.Docs("docs/json.md#decimals-in-one-spelling");
        const string Smallest = "smallest-scale", Covers = "covers-every-decimal", Reads = "reads-a-number-or-a-string", Keeps = "keeps-the-scale-it-reads";
        var kindOf = number ? "number-is-a-number" : "string-is-a-string";

        // Writing: one spelling, the smallest scale, as a number or a string; the value object's document is the decimal's.
        var (jp, xp) = Jx.Ser(hp, o); var (jv, xv) = Jx.Ser(hv, o); var (jb, xb) = Jx.Ser(hp, ob);
        if (xb is not null && xp is not null && (xv is not null || ownVo) && xp.GetType() == xb.GetType())
        {
            // the options refuse the shape for the serializer's own decimal too: nothing is written or read to compare. Where the value
            // object is refused alike, at the same stage with the same exception type, the decimal without TypeKit's converter is side (1)
            // of the three-sided differential (Sides), and each check is the framework's refusal; otherwise none can tell.
            var one = new Sides.Answer("write", null, xb); var three = new Sides.Answer("write", null, xv);
            if (Sides.Alike(one, three))
            {
                var framework = Sides.Measured(one, "the serializer's own decimal, without TypeKit's converter");
                foreach (var o2 in new[] { Smallest, kindOf, Covers, Reads, Keeps }) r.Check(o2, framework.Baseline, c => Sides.Refuses(c, framework, three));
                return null;
            }
            var why = "the options refuse the shape for the serializer's own decimal too: " + Jx.Ex(xb);
            string? None(Check c) => c.Inconclusive(why);
            foreach (var ob2 in new[] { Smallest, kindOf }) r.Check(ob2, docs, None);
            r.Check(Covers, Baseline.Twin, None); r.Check(Reads, docs, None); r.Check(Keeps, docs, None);
            return null;
        }
        var tp = xp is null ? Jx.Tokens(jp!).Where(t => IsDecimal(t, d)).ToList() : [];
        var tb = xb is null ? Jx.Tokens(jb!).Where(t => IsDecimal(t, d)).ToList() : null;
        var writeLeftOut = tb is { Count: 0 } && xp is null && tp.Count == 0;
        string? WriteLeftOut(Check c) { c.Note(NoteKeys.JsonMemberLeftOutByOptions, "write: " + Jx.Cut(jb)); return c.Inconclusive("write: the options leave the member out, for the serializer's own decimal too: " + Jx.Cut(jb)); }
        var want = number ? JsonTokenType.Number : JsonTokenType.String;
        var f1 = r.Check(Smallest, docs, c =>
        {
            if (xp is not null) return $"write: the decimal with {conv.GetType().Name} threw {Jx.Ex(xp)}; without it the serializer wrote {Jx.Cut(jb)}";
            if (tb is { Count: 0 } && tp.Count != 0) return $"write: the options leave the member out without the converter ({Jx.Cut(jb)}) but it wrote {Jx.Cut(jp)}";
            if (writeLeftOut) return WriteLeftOut(c);
            if (tp.Count == 0) return $"write: no decimal in {Jx.Cut(jp)}; the serializer alone wrote {Jx.Cut(jb)}";
            foreach (var t in tp.Where(t => t.Type != JsonTokenType.PropertyName))
                if (t.Raw != canon) return $"write: {t.Type} {t.Raw}, expected the smallest scale {canon}, in {Jx.Cut(jp)}";
            c.Note(NoteKeys.Compared, $"write {text} -> {canon}");
            return null;
        });
        var f2 = r.Check(kindOf, docs, c =>
        {
            if (xp is not null) return c.Inconclusive("write: the decimal with the converter threw, judged by smallest-scale");
            if (writeLeftOut) return WriteLeftOut(c);
            var values = tp.Where(t => t.Type != JsonTokenType.PropertyName).ToList();
            if (values.Count == 0) return c.Inconclusive($"write: no decimal value in {Jx.Cut(jp)}, only keys, which are names");
            foreach (var t in values)
                if (t.Type != want) return $"write: {t.Type} {t.Raw}, expected the {want} {canon}, in {Jx.Cut(jp)}";
            c.Note(NoteKeys.Compared, $"write as a {want}");
            return null;
        });

        // Reading: a number, a string holding one, and an escaped string all read, the scale they carry kept, on both sides alike.
        var full = Jx.Full(o);
        var (jf, xf) = Jx.Ser(hp, full);
        if (xf is not null) { r.Outcome("harness-error", "the document with every member written threw " + Jx.Ex(xf)); return f1 ?? f2; }
        var at = Jx.Tokens(jf!).Where(t => IsDecimal(t, d)).ToList();
        string? readNothing = null;
        var forms = new List<Form>();
        var leftOut = false;
        if (at.Count == 0)
        {
            // Nothing to read either: the options leave the member out of every document (a field without IncludeFields).
            var (jfb, _) = Jx.Ser(hp, Jx.Full(ob));
            if (jfb is null || Jx.Tokens(jfb).Any(t => IsDecimal(t, d))) { r.Outcome("harness-error", "no decimal in the document with every member written: " + jf); return f1 ?? f2; }
            readNothing = "read: the options leave the member out of every document, for the serializer's own decimal too: " + Jx.Cut(jf);
        }
        else
        {
            var key = at.Any(t => t.Type == JsonTokenType.PropertyName);
            var texts = new List<(string Name, string Json)>();
            if (!key) texts.Add(("number", Jx.Rewrite(jf!, at, _ => text)));
            texts.Add(("string", Jx.Rewrite(jf!, at, _ => "\"" + text + "\"")));
            texts.Add(("escaped string", Jx.Rewrite(jf!, at, _ => "\"" + Bs + "u00" + ((int)text[0]).ToString("x2") + text[1..] + "\"")));
            // The baseline reads the first form with the serializer's own decimal: it shows whether these options read the member at all.
            var (vb, eb) = Jx.De(texts[0].Json, hp.Declared, ob);
            leftOut = eb is null && Held(vb, hp.Declared, d).Count == 0;
            foreach (var (name, json) in texts)
            {
                var (vp, ep) = Jx.De(json, hp.Declared, o);
                var (vv, ev) = ownVo ? (null, null) : Jx.De(json, hv.Declared, o);
                var both = ep is not null && eb is not null && (ownVo || ev is not null) && ep.GetType() == eb.GetType();
                var cp = ep is null ? Held(vp, hp.Declared, d) : []; var cv = ownVo ? cp : ev is null ? Held(vv, hv.Declared, d) : [];
                forms.Add(new(name, json, ep, ev, cp, cv, both));
            }
            if (leftOut && forms.All(f => f.Ep is null && f.Ev is null && f.Cp.Count == 0 && f.Cv.Count == 0))
                readNothing = "read: the options read no member, for the serializer's own decimal too";
        }
        string? ReadLeftOut(Check c) { c.Note(NoteKeys.JsonMemberLeftOutByOptions, "read"); return c.Inconclusive(readNothing!); }
        // The forms both sides refuse alike under these options (the serializer's own decimal too) have nothing to compare.
        var compared = forms.Where(f => !f.BothRefused).ToList();
        string? AllRefused(Check c)
        {
            foreach (var f in forms.Where(f => f.BothRefused)) c.Log.AppendLine($"read {f.Name}: both refuse: {Jx.Ex(f.Ep!)}");
            return c.Inconclusive("read: every form is refused for the serializer's own decimal too: " + Jx.Ex(forms[0].Ep!));
        }
        var f4 = r.Check(Reads, docs, c =>
        {
            if (readNothing is not null) return ReadLeftOut(c);
            foreach (var f in forms)
            {
                if (f.BothRefused) continue;
                if (f.Ep is not null) return $"read {f.Name}: the decimal with {conv.GetType().Name} threw {Jx.Ex(f.Ep)}\n  text={Jx.Cut(f.Json)}";
                if (leftOut) { if (f.Cp.Count != 0) return $"read {f.Name}: the options read no member for the serializer's own decimal, yet it read {Show(f.Cp)}"; continue; }
                if (f.Cp.Count == 0) return $"read {f.Name}: the decimal read nothing\n  text={Jx.Cut(f.Json)}";
            }
            if (compared.Count == 0) return AllRefused(c);
            c.Note(NoteKeys.Compared, "read " + string.Join(", ", compared.Select(f => f.Name)));
            return null;
        });
        var f5 = r.Check(Keeps, docs, c =>
        {
            if (readNothing is not null) return ReadLeftOut(c);
            if (compared.Count == 0) return AllRefused(c);
            var kept = 0;
            foreach (var f in compared)
            {
                if (f.Cp.Count == 0) continue;   // read nothing, or refused: reads-a-number-or-a-string judges it
                if (f.Cp.Any(x => !Same(x, d))) return $"read {f.Name}: read {Show(f.Cp)}, expected {d.ToString(Inv)} with its scale\n  text={Jx.Cut(f.Json)}";
                kept++;
            }
            if (kept == 0) return c.Inconclusive("read: no form read a decimal to hold its scale to");
            c.Note(NoteKeys.Compared, $"read with the scale of {text}");
            return null;
        });
        var f3 = r.Check(Covers, ownVo ? Baseline.RoundTrip : Baseline.Twin, c =>
        {
            if (xv is not null && !ownVo) return $"write: the value object threw {Jx.Ex(xv)} where the decimal wrote {Jx.Cut(jp)}";
            if (xp is not null) return c.Inconclusive("write: the decimal with the converter threw, judged by smallest-scale");
            if (tb is not null && tb.Count != 0 && tp.Count != tb.Count) return $"write: {tp.Count} decimals in {Jx.Cut(jp)}, {tb.Count} in {Jx.Cut(jb)}";
            foreach (var t in tp.Where(t => t.Type == JsonTokenType.PropertyName))
                if (t.Raw != canon) return $"write: key {t.Raw}, expected {canon}, in {Jx.Cut(jp)}";
            if (ownVo)
            {
                // "Your own converter wins" (AspNetCore README#what-the-document-says): the application's converter writes the value object,
                // so the decimal's document is none of its; its own document reads back under the same options
                c.Note(NoteKeys.OwnVo);
                var (_, xr) = Jx.De(jv!, hv.Declared, o);
                if (xr is not null) return "own converter for the value object: its document does not read back: " + Jx.Ex(xr);
                return null;
            }
            if (jv != jp) return $"write: the value object wrote {Jx.Cut(jv)}, the decimal {Jx.Cut(jp)}";
            foreach (var f in compared)
            {
                if (f.Ep is not null) continue;   // the decimal refused it: reads-a-number-or-a-string judges it
                if (f.Ev is not null) return $"read {f.Name}: the value object threw {Jx.Ex(f.Ev)} where the decimal read it\n  text={Jx.Cut(f.Json)}";
                if (f.Cp.Count != f.Cv.Count || f.Cp.Zip(f.Cv).Any(z => !Same(z.First, z.Second))) return $"read {f.Name}: the value object read {Show(f.Cv)}, the decimal {Show(f.Cp)}\n  text={Jx.Cut(f.Json)}";
            }
            if (writeLeftOut && (readNothing is not null || compared.Count == 0)) return WriteLeftOut(c);
            c.Note(NoteKeys.Compared, $"{(number ? "number" : "string")} {text} -> {canon}, the value object's document the decimal's");
            return null;
        });
        return f1 ?? f2 ?? f3 ?? f4 ?? f5;
    }
}
