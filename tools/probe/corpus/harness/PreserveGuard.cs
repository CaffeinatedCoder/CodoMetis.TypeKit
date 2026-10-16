// Claim C10-preserve-guard (src/CodoMetis.TypeKit.Generators/README.md#json): "Preserved references (ReferenceHandler.Preserve) throw a
// NotSupportedException where the serializer could write $id or $ref inside the underlying value. That's a class it walks as an object [...]
// These are unaffected: a value the serializer writes with a value converter (a number, a string, a Guid, a Uri), anything a converter of
// your own writes, on the options or on one property, a struct of such values, and ReferenceHandler.IgnoreCycles."
// Oracle: claim for the guarded rows (a value object over a class the serializer walks, under Preserve, with no converter of the
// application's for it: NotSupportedException naming the type and Preserve, both directions, never a document); differential for the
// unaffected rows (the value object's document is the wrapped type's under the same options, never an $id twice, and every read agrees
// with the wrapped type's, a refusal being the section's: a JsonException naming the value object, with no inner exception).
using System.Text.Json;
using Probes.Shapes;
namespace Probes.Json;

public static class PreserveGuard
{
    public static string? Check(Row r)
    {
        var handler = r["handler"];
        var want = handler == "none" ? "default" : handler;
        var row = r;
        if (r.Get("json.ReferenceHandler") is { } jr && jr != want)
        {
            // one setting under two names: the claim's handler decides (claims.json excludes the differing pairs)
            r.Substituted("json.ReferenceHandler", want, "handler is the same setting");
            row = r.With("json.ReferenceHandler", want);
        }
        var kind = r["shape.kind"];
        var e = kind == "closed-set" ? Registry.Get(kind, "-") : Registry.Get(kind, r["shape.wrapped"]);
        var position = r["shape.position"];
        var containing = position == "root" ? "class" : r["shape.containing"];
        var plain = e.Valid(); var voValue = Make.Vo(e, plain);
        var hp = Pos.Build(position, containing, e, false, plain);
        var hv = Pos.Build(position, containing, e, true, voValue);
        var o = JCtx.Apply(row, e, C10JsonContext.Default, hp.Declared, hv.Declared);
        // A closed set travels as its name, which no naming policy renames and no converter for strings writes (docs/closed-sets.md).
        var op = kind == "closed-set" && (row.Get("json.DictionaryKeyPolicy") is not (null or "default") || row.Is("json.Converters", "own-for-wrapped"))
            ? JCtx.Apply(row.With("json.DictionaryKeyPolicy", "default").With("json.Converters", row.Is("json.Converters", "own-for-wrapped") ? "default" : row["json.Converters"]), e, C10JsonContext.Default, hp.Declared, hv.Declared)
            : o;
        var ownWrapped = row.Is("json.Converters", "own-for-wrapped");
        var ownVo = row.Is("json.Converters", "own-for-value-object");
        var guarded = want == "Preserve" && e.Wrapped == "own-record-class" && !ownWrapped && !ownVo;
        return guarded ? Guarded(r, e, hp, hv, o) : Unaffected(r, e, hp, hv, op, o, ownVo);
    }

    static string? Guarded(Row r, Entry e, Held hp, Held hv, JsonSerializerOptions o)
    {
        var wrapped = Make.Held(e.Plain);
        string? Refusal(Exception? x, string what)
        {
            if (x is not NotSupportedException) return $"{what}: expected NotSupportedException naming {wrapped.Name} under ReferenceHandler.Preserve, got {(x is null ? "no exception" : Jx.Ex(x))}";
            if (!x.Message.Contains(wrapped.FullName!) || !x.Message.Contains("ReferenceHandler.Preserve")) return $"{what}: the NotSupportedException does not name {wrapped.FullName} and ReferenceHandler.Preserve: {x.Message}";
            return null;
        }
        var (jv, xv) = Jx.Ser(hv, o);
        var (jp, xp) = Jx.Ser(hp, o);
        if (jv is not null)
        {
            // the options may leave the member out of the document, for the wrapped type too: then no converter ran
            if (jp is not null && !Jx.Tokens(jp).Any(t => t.Text == "x") && !Jx.Tokens(jv).Any(t => t.Text == "x")) r.Note(NoteKeys.JsonMemberLeftOutByOptions, "write: " + Jx.Cut(jv));
            else return Refusal(null, "write") + $"; it wrote {Jx.Cut(jv)}" + (DuplicateId(jv) is { } dup ? $" with {dup} twice" : "");
        }
        else if (Refusal(xv, "write") is { } f) return f;
        if (xp is not null) { r.Note(NoteKeys.BothThrew, "the wrapped type's document under these options: " + Jx.Ex(xp)); return null; }
        // reading: the wrapped type's own document, with the row's input applied
        var input = r["input.json"];
        var text = JsonParity.InputFor(input, e, Jx.Full(o), Jx.Ser(hp, Jx.Full(o)).Json ?? jp!, r["shape.position"]);
        var (vp, ep) = Jx.De(text, hp.Declared, o);
        var (vv, ev) = Jx.De(text, hv.Declared, o);
        if (ev is null)
        {
            // no converter ran: the read holds none of the wrapped value (a JSON null for a class, a member the options do not read)
            if (vv is not null && Jx.Tokens(JsonSerializer.Serialize(vv, hv.Declared, Jx.Plain)).Any(t => t.Text == "x"))
                return $"read {input}: the value object over {Make.Held(e.Plain).Name} read a document under Preserve: {Jx.Cut(text)}";
            r.Note(NoteKeys.JsonMemberLeftOutByOptions, "read " + input);
            return null;
        }
        if (ev is NotSupportedException) return Refusal(ev, "read " + input);
        if (ep is not null && ev.GetType() == ep.GetType() && ev.Message == ep.Message) { r.Note(NoteKeys.JsonReadRefusedByStjAlone, input + ": " + Jx.Ex(ev)); return null; }
        if (ev is JsonException && ev.Message.Contains(Make.Held(e.Vo).Name) && ev.InnerException is null) { r.Note(NoteKeys.Compared, "read " + input + ": refused naming the value object before the guard"); return null; }
        return Refusal(ev, "read " + input) + $"\n  text={Jx.Cut(text)}";
    }

    static string? Unaffected(Row r, Entry e, Held hp, Held hv, JsonSerializerOptions op, JsonSerializerOptions o, bool ownVo)
    {
        var (jp, xp) = Jx.Ser(hp, op); var (jv, xv) = Jx.Ser(hv, o);
        // MaxDepth=3: a converter costs a level of depth, the serializer's or the generated one (design note D1, the catalogue's stj-maxdepth)
        if (Jx.Depth(r, xv) || Jx.Depth(r, xp)) { r.Note(NoteKeys.StjMaxdepth, "write: " + Jx.Ex((xv ?? xp)!)); return null; }
        if (xp is not null && xv is not null)
        {
            if (xp.GetType() == xv.GetType() && xp.Message == xv.Message) { r.Note(NoteKeys.BothThrew, "write: " + Jx.Ex(xp)); return null; }
            return $"write: both threw, differently: the wrapped type {Jx.Ex(xp)}, the value object {Jx.Ex(xv)}";
        }
        if (xv is not null) return $"write: the value object threw {Jx.Ex(xv)} where the wrapped type wrote {Jx.Cut(jp)}";
        if (xp is not null) return $"write: the value object wrote {Jx.Cut(jv)} where the wrapped type threw {Jx.Ex(xp)}";
        if (DuplicateId(jv!) is { } dup) return $"write: $id {dup} twice in {Jx.Cut(jv)}";
        if (ownVo)
        {
            r.Note(NoteKeys.OwnVo);   // the application's converter for the value object writes its own document; it reads it back, unaffected
            var (jf, _) = Jx.Ser(hv, Jx.Full(o));
            var (_, xr) = Jx.De(jf ?? jv!, hv.Declared, o);
            if (xr is not null) return "own converter for the value object: its document does not read back: " + Jx.Ex(xr);
            return null;   // input.json rewrites the wrapped type's document, which this converter does not read (claims.json excludes the pairs)
        }
        if (jv != jp)
        {
            if (e.Wrapped == "byte" && r.Is("shape.position", "array-element"))
            {
                // byte[] is base64 by the serializer's own rule, an array of value objects a JSON array: unaffected means the value
                // object's document reads back as written
                var (back, xb) = Jx.De(jv!, hv.Declared, o);
                if (xb is not null) return "write: the array of byte value objects does not read back: " + Jx.Ex(xb);
                var again = JsonSerializer.Serialize(back, hv.Declared, o);
                if (again != jv) return $"round trip: {Jx.Cut(jv)} read back as {Jx.Cut(again)}";
                r.Note(NoteKeys.Compared, "an array of byte value objects round-trips as a JSON array");
                return null;
            }
            return $"write: the value object wrote {Jx.Cut(jv)}, the wrapped type {Jx.Cut(jp)}";
        }
        var input = r["input.json"];
        var full = Jx.Ser(hp, Jx.Full(op)).Json ?? jp!;
        var text = JsonParity.InputFor(input, e, Jx.Full(op), full, r["shape.position"]);
        return ReadAlike.Check(r, e, input, text, hp, hv, op, ownVo ? null : o);
    }

    /// <summary>The first $id value a document holds twice, or null.</summary>
    public static string? DuplicateId(string json)
    {
        var t = Jx.Tokens(json); var seen = new HashSet<string>();
        for (var i = 0; i + 1 < t.Count; i++)
            if (t[i].Type == JsonTokenType.PropertyName && t[i].Text == "$id" && !seen.Add(t[i + 1].Text ?? t[i + 1].Raw)) return t[i + 1].Text;
        return null;
    }
}

/// <summary>
/// One read of the same text into the wrapped type's shape and the value object's: they agree, or differ only as the documents say
/// (a struct value object refuses a JSON null, a sealed record reads it as null, a validated one refuses what Create refuses, a closed set
/// has only its members); a refusal of the value object's is a JsonException naming it, with no inner exception (Generators README#json).
/// </summary>
public static class ReadAlike
{
    public static string? Check(Row r, Entry e, string input, string text, Held hp, Held hv, JsonSerializerOptions op, JsonSerializerOptions? ov)
    {
        var (vp, ep) = Jx.De(text, hp.Declared, op);
        if (ov is null) { if (ep is null) r.Note(NoteKeys.Compared, "read " + input + " (the wrapped type; the value object's converter is the application's)"); else r.Note(NoteKeys.ReadBothThrew, Jx.Ex(ep)); return null; }
        var (vv, ev) = Jx.De(text, hv.Declared, ov);
        var voName = Make.Held(e.Vo).Name;
        string? Named(Exception x) => x is JsonException && x.Message.Contains(voName) && x.InnerException is null ? null
            : $"read {input}: the value object's refusal is {Jx.Ex(x)}{(x.InnerException is null ? "" : " with an inner exception")}, not a JsonException naming {voName}\n  text={Jx.Cut(text)}";
        var stored = r.Is("json.Converters", "StoredJsonConverterFactory");
        if (Jx.Depth(r, ev) || Jx.Depth(r, ep)) { r.Note(NoteKeys.StjMaxdepth, "read " + input + ": " + Jx.Ex((ev ?? ep)!)); return null; }
        if (ep is null && ev is null)
        {
            if (e.Kind.StartsWith("validated") && input == "refused-by-create" && !stored && e.Refused() is not null && text != Jx.Ser(hp, op).Json && !r.Is("shape.position", "dictionary-key"))
                return $"read {input}: the validated value object read what Create refuses\n  text={Jx.Cut(text)}";
            var sp = JsonSerializer.Serialize(vp, hp.Declared, op); var sv = JsonSerializer.Serialize(vv, hv.Declared, ov);
            if (sp != sv) return $"read {input}: read back differs: wrapped {Jx.Cut(sp)} vs value object {Jx.Cut(sv)}\n  text={Jx.Cut(text)}";
            r.Note(NoteKeys.Compared, "read " + input); return null;
        }
        if (ep is not null && ev is not null)
        {
            if (ep.GetType() == ev.GetType() && Jx.Swap(ep.Message, Make.Held(e.Plain), Make.Held(e.Vo)) == ev.Message) { r.Note(NoteKeys.JsonReadRefusedByStjAlone, input + ": " + Jx.Ex(ev)); return null; }
            if (Named(ev) is { } f) return f + $"; the wrapped type: {Jx.Ex(ep)}";
            r.Note(NoteKeys.Compared, "read " + input + ": both refuse, the value object naming itself"); return null;
        }
        if (ev is not null)
        {
            var intended = (input == "null" && e.Vo.IsValueType) || (e.Kind.StartsWith("validated") && input == "refused-by-create" && !stored) || (e.Kind == "closed-set" && input != "valid");
            if (!intended) return $"read {input}: the wrapped type read it, the value object threw {Jx.Ex(ev)}\n  text={Jx.Cut(text)}";
            if (Named(ev) is { } f) return f;
            r.Note(NoteKeys.Compared, "read " + input + ": refused as documented"); return null;
        }
        // the wrapped type refused and the value object read: only a JSON null for a sealed record, which reads it as any class does
        if (input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType && Jx.Tokens(JsonSerializer.Serialize(vv, hv.Declared, Jx.Plain)).Any(t => t.Type == JsonTokenType.Null))
        { r.Note(NoteKeys.Compared, "read null: the sealed record reads null, as any class does"); return null; }
        return $"read {input}: the value object read what the wrapped type refused ({Jx.Ex(ep!)})\n  text={Jx.Cut(text)}";
    }
}
