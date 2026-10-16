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
        var hp = Pos.Build(position, containing, e, false, d);
        var hv = Pos.Build(position, containing, e, true, Make.Vo(e, d));
        var o = JCtx.Apply(r, e, C01JsonContext.Default, hp.Declared, hv.Declared);
        var ob = new JsonSerializerOptions(o);      // the baseline: the same options, the serializer's own decimal
        foreach (var c in ob.Converters.Where(c => c.CanConvert(typeof(decimal))).ToList()) ob.Converters.Remove(c);
        o.Converters.Insert(0, conv);               // registered first: an application converter for decimals (own-for-wrapped) comes after it
        var ownVo = r.Is("json.Converters", "own-for-value-object");

        // Writing: one spelling, the smallest scale, as a number or a string; the value object's document is the decimal's.
        var (jp, xp) = Jx.Ser(hp, o); var (jv, xv) = Jx.Ser(hv, o); var (jb, xb) = Jx.Ser(hp, ob);
        var wrote = false;
        if (xb is not null && xp is not null && (xv is not null || ownVo) && xp.GetType() == xb.GetType())
        { r.Note(NoteKeys.BothThrew, "write: " + Jx.Ex(xb)); return null; }   // the options refuse the shape for the serializer's own decimal too
        if (xp is not null) return $"write: the decimal with {conv.GetType().Name} threw {Jx.Ex(xp)}; without it the serializer wrote {Jx.Cut(jb)}";
        if (xv is not null && !ownVo) return $"write: the value object threw {Jx.Ex(xv)} where the decimal wrote {Jx.Cut(jp)}";
        var tp = Jx.Tokens(jp!).Where(t => IsDecimal(t, d)).ToList();
        var tb = xb is null ? Jx.Tokens(jb!).Where(t => IsDecimal(t, d)).ToList() : null;
        if (tb is { Count: 0 })
        {
            if (tp.Count != 0) return $"write: the options leave the member out without the converter ({Jx.Cut(jb)}) but it wrote {Jx.Cut(jp)}";
            r.Note(NoteKeys.JsonMemberLeftOutByOptions, "write: " + Jx.Cut(jb));
        }
        else
        {
            if (tp.Count == 0) return $"write: no decimal in {Jx.Cut(jp)}; the serializer alone wrote {Jx.Cut(jb)}";
            if (tb is not null && tp.Count != tb.Count) return $"write: {tp.Count} decimals in {Jx.Cut(jp)}, {tb.Count} in {Jx.Cut(jb)}";
            foreach (var t in tp)
            {
                if (t.Type == JsonTokenType.PropertyName) { if (t.Raw != canon) return $"write: key {t.Raw}, expected {canon}, in {Jx.Cut(jp)}"; continue; }
                var want = number ? JsonTokenType.Number : JsonTokenType.String;
                if (t.Type != want || t.Raw != canon) return $"write: {t.Type} {t.Raw}, expected the {want} {canon}, in {Jx.Cut(jp)}";
            }
            wrote = true;
        }
        if (ownVo)
        {
            r.Note(NoteKeys.OwnVo);   // the application's converter for the value object writes its own document; it reads it back
            var (_, xr) = Jx.De(jv!, hv.Declared, o);
            if (xr is not null) return "own converter for the value object: its document does not read back: " + Jx.Ex(xr);
        }
        else if (jv != jp) return $"write: the value object wrote {Jx.Cut(jv)}, the decimal {Jx.Cut(jp)}";

        // Reading: a number, a string holding one, and an escaped string all read, the scale they carry kept, on both sides alike.
        var full = Jx.Full(o);
        var (jf, xf) = Jx.Ser(hp, full);
        if (xf is not null) { r.Outcome("harness-error", "the document with every member written threw " + Jx.Ex(xf)); return null; }
        var at = Jx.Tokens(jf!).Where(t => IsDecimal(t, d)).ToList();
        if (at.Count == 0)
        {
            // Nothing to read either: the options leave the member out of every document (a field without IncludeFields).
            var (jfb, _) = Jx.Ser(hp, Jx.Full(ob));
            if (jfb is null || Jx.Tokens(jfb).Any(t => IsDecimal(t, d))) { r.Outcome("harness-error", "no decimal in the document with every member written: " + jf); return null; }
            if (!wrote) r.Note(NoteKeys.JsonBothDirectionsLeftOutByOptions, jf);
            return null;
        }
        var key = at.Any(t => t.Type == JsonTokenType.PropertyName);
        var forms = new List<(string Name, string Json)>();
        if (!key) forms.Add(("number", Jx.Rewrite(jf!, at, _ => text)));
        forms.Add(("string", Jx.Rewrite(jf!, at, _ => "\"" + text + "\"")));
        forms.Add(("escaped string", Jx.Rewrite(jf!, at, _ => "\"" + Bs + "u00" + ((int)text[0]).ToString("x2") + text[1..] + "\"")));
        // The baseline reads the first form with the serializer's own decimal: it shows whether these options read the member at all.
        var (vb, eb) = Jx.De(forms[0].Json, hp.Declared, ob);
        var leftOut = eb is null && Held(vb, hp.Declared, d).Count == 0;
        var read = false;
        foreach (var (name, json) in forms)
        {
            var (vp, ep) = Jx.De(json, hp.Declared, o);
            var (vv, ev) = ownVo ? (null, null) : Jx.De(json, hv.Declared, o);
            if (ep is not null)
            {
                if (eb is not null && (ownVo || ev is not null) && ep.GetType() == eb.GetType()) { r.Note(NoteKeys.ReadBothThrew, name + ": " + Jx.Ex(eb)); continue; }
                return $"read {name}: the decimal with {conv.GetType().Name} threw {Jx.Ex(ep)}\n  text={Jx.Cut(json)}";
            }
            if (ev is not null) return $"read {name}: the value object threw {Jx.Ex(ev)} where the decimal read it\n  text={Jx.Cut(json)}";
            var cp = Held(vp, hp.Declared, d); var cv = ownVo ? cp : Held(vv, hv.Declared, d);
            if (leftOut)
            {
                if (cp.Count != 0 || cv.Count != 0) return $"read {name}: the options read no member for the serializer's own decimal, yet it read {Show(cp)} / {Show(cv)}";
                r.Note(NoteKeys.JsonMemberLeftOutByOptions, "read: " + name);
                continue;
            }
            if (cp.Count == 0) return $"read {name}: the decimal read nothing\n  text={Jx.Cut(json)}";
            if (cp.Any(x => !Same(x, d))) return $"read {name}: read {Show(cp)}, expected {d.ToString(Inv)} with its scale\n  text={Jx.Cut(json)}";
            if (cp.Count != cv.Count || cp.Zip(cv).Any(z => !Same(z.First, z.Second))) return $"read {name}: the value object read {Show(cv)}, the decimal {Show(cp)}\n  text={Jx.Cut(json)}";
            read = true;
        }
        if (!wrote && !read) r.Note(NoteKeys.JsonBothDirectionsLeftOutByOptions);
        r.Note(NoteKeys.Compared, $"{(number ? "number" : "string")} {text} -> {canon}");
        return null;
    }
}
