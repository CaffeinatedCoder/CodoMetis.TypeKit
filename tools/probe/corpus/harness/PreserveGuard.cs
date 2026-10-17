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
        _ = r["input.json"];   // the row's input, which every check of the row runs under; the reads apply it
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
        // The obligation the row's handler and converters select: a converter of the application's, the guarded class under Preserve,
        // IgnoreCycles, and otherwise a value the serializer writes with a value converter, under Preserve or with no handler at all.
        var obligation = ownWrapped || ownVo ? "own-converters-unaffected" : guarded ? "refused-where-references-could-be-written"
                       : want == "IgnoreCycles" ? "ignore-cycles-unaffected" : "value-converters-unaffected";
        return guarded ? Guarded(r, obligation, e, hp, hv, o) : Unaffected(r, obligation, e, hp, hv, op, o, ownVo);
    }

    /// <summary>The guarded rows: NotSupportedException naming the wrapped type and Preserve, in both directions, never a document.</summary>
    static string? Guarded(Row r, string obligation, Entry e, Held hp, Held hv, JsonSerializerOptions o)
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
        var fw = r.Check(obligation, Baseline.Refusal, c =>
        {
            if (jv is not null)
            {
                // the options may leave the member out of the document, for the wrapped type too: then no converter ran, and nothing was refused
                if (jp is not null && !Jx.Tokens(jp).Any(t => t.Text == "x") && !Jx.Tokens(jv).Any(t => t.Text == "x"))
                { c.Note(NoteKeys.JsonMemberLeftOutByOptions, "write: " + Jx.Cut(jv)); return c.Inconclusive("write: the options leave the member out, for the wrapped type too: " + Jx.Cut(jv)); }
                return Refusal(null, "write") + $"; it wrote {Jx.Cut(jv)}" + (DuplicateId(jv) is { } dup ? $" with {dup} twice" : "");
            }
            if (Refusal(xv, "write") is { } f) return f;
            c.Note(NoteKeys.Compared, "write refused");
            return null;
        });
        var fr = r.Check(obligation, Baseline.Refusal, c =>
        {
            if (xp is not null) return c.Inconclusive("read: the wrapped type's document is refused under these options, so there is none to read: " + Jx.Ex(xp));
            // reading: the wrapped type's own document, with the row's input applied
            var input = c["input.json"];
            var text = JsonParity.InputFor(input, e, Jx.Full(o), Jx.Ser(hp, Jx.Full(o)).Json ?? jp!, c["shape.position"]);
            var (vp, ep) = Jx.De(text, hp.Declared, o);
            var (vv, ev) = Jx.De(text, hv.Declared, o);
            if (ev is null)
            {
                // no converter ran: the read holds none of the wrapped value (a JSON null for a class, a member the options do not read)
                if (vv is not null && Jx.Tokens(JsonSerializer.Serialize(vv, hv.Declared, Jx.Plain)).Any(t => t.Text == "x"))
                    return $"read {input}: the value object over {Make.Held(e.Plain).Name} read a document under Preserve: {Jx.Cut(text)}";
                c.Note(NoteKeys.JsonMemberLeftOutByOptions, "read " + input);
                return c.Inconclusive($"read {input}: no converter ran, the read holds none of the wrapped value");
            }
            if (ev is NotSupportedException) { if (Refusal(ev, "read " + input) is { } f) return f; c.Note(NoteKeys.Compared, "read " + input + " refused"); return null; }
            // the serializer refused the document before any converter ran, the wrapped type's alike: the guard was never reached
            if (ep is not null && ev.GetType() == ep.GetType() && ev.Message == ep.Message)
            { c.Note(NoteKeys.JsonReadRefusedByStjAlone, input + ": " + Jx.Ex(ev)); return c.Inconclusive($"read {input}: the serializer refuses the document before the guard, the wrapped type's alike: {Jx.Ex(ev)}"); }
            if (ev is JsonException && ev.Message.Contains(Make.Held(e.Vo).Name) && ev.InnerException is null) { c.Note(NoteKeys.Compared, "read " + input + ": refused naming the value object before the guard"); return null; }
            return Refusal(ev, "read " + input) + $"\n  text={Jx.Cut(text)}";
        });
        return fw ?? fr;
    }

    /// <summary>
    /// The unaffected rows: the value object's document is the wrapped type's under the same options, never an $id twice, and its read
    /// agrees with the wrapped type's (ReadAlike); under a converter of the application's for the value object, that converter's document
    /// reads back and reads the row's input alike with and without the reference handler.
    /// </summary>
    static string? Unaffected(Row r, string obligation, Entry e, Held hp, Held hv, JsonSerializerOptions op, JsonSerializerOptions o, bool ownVo)
    {
        var (jp, xp) = Jx.Ser(hp, op); var (jv, xv) = Jx.Ser(hv, o);
        // byte[] is base64 by the serializer's own rule (KB-038), an array of value objects a JSON array: unaffected means the value
        // object's document reads back as written
        var byteArray = e.Wrapped == "byte" && r.Is("shape.position", "array-element") && jv is not null && jp is not null && jv != jp;
        var fw = r.Check(obligation, ownVo || byteArray ? Baseline.RoundTrip : Baseline.Twin, c =>
        {
            // MaxDepth=3: a converter costs a level of depth, the serializer's or the generated one (design note D1, the catalogue's stj-maxdepth);
            // Jx.Depth is a text-matching site (JsonFamily.cs)
            if (Jx.Depth(r, xv) || Jx.Depth(r, xp)) { c.Note(NoteKeys.StjMaxdepth, "write: " + Jx.Ex((xv ?? xp)!)); return null; }
            if (xp is not null && xv is not null)
            {
                if (xp.GetType() == xv.GetType() && xp.Message == xv.Message) return c.Inconclusive("write: the options refuse the shape, for the wrapped type alike: " + Jx.Ex(xp));
                return $"write: both threw, differently: the wrapped type {Jx.Ex(xp)}, the value object {Jx.Ex(xv)}";
            }
            if (xv is not null) return $"write: the value object threw {Jx.Ex(xv)} where the wrapped type wrote {Jx.Cut(jp)}";
            if (xp is not null) return $"write: the value object wrote {Jx.Cut(jv)} where the wrapped type threw {Jx.Ex(xp)}";
            if (DuplicateId(jv!) is { } dup) return $"write: $id {dup} twice in {Jx.Cut(jv)}";
            if (ownVo)
            {
                c.Note(NoteKeys.OwnVo);   // the application's converter for the value object writes its own document; it reads it back, unaffected
                var (jf, _) = Jx.Ser(hv, Jx.Full(o));
                var (_, xr) = Jx.De(jf ?? jv!, hv.Declared, o);
                if (xr is not null) return "own converter for the value object: its document does not read back: " + Jx.Ex(xr);
                return null;
            }
            if (byteArray)
            {
                var (back, xb) = Jx.De(jv!, hv.Declared, o);
                if (xb is not null) return "write: the array of byte value objects does not read back: " + Jx.Ex(xb);
                var again = JsonSerializer.Serialize(back, hv.Declared, o);
                if (again != jv) return $"round trip: {Jx.Cut(jv)} read back as {Jx.Cut(again)}";
                c.Note(NoteKeys.Compared, "an array of byte value objects round-trips as a JSON array");
                return null;
            }
            if (jv != jp) return $"write: the value object wrote {Jx.Cut(jv)}, the wrapped type {Jx.Cut(jp)}";
            c.Note(NoteKeys.Compared, "write");
            return null;
        });
        var fr = r.Check(obligation, ownVo ? Baseline.RoundTrip : Baseline.Twin, c =>
        {
            if (ownVo) return OwnReadsInput(r, c, e, hv, o);
            // the wrapped type's base64 document is no array of value objects (KB-038): the round trip above holds the value object to its own
            if (byteArray) return c.Inapplicable(Cite.Fact("KB-038"), "read: the wrapped type's document is a base64 string");
            // the document is refused by depth (Jx.Depth, the text-matching site), so its read is too: the write's refusal, as it is
            if (Jx.Depth(r, xv) || Jx.Depth(r, xp)) { c.Note(NoteKeys.StjMaxdepth, "read: " + Jx.Ex((xv ?? xp)!)); return null; }
            var full = Jx.Ser(hp, Jx.Full(op)).Json ?? jp;
            if (full is null) return c.Inconclusive("read: the wrapped type is not written under these options, so there is no document to read");
            var input = c["input.json"];
            var text = JsonParity.InputFor(input, e, Jx.Full(op), full, c["shape.position"]);
            return ReadAlike.Check(r, c, e, input, text, hp, hv, op, o);
        });
        return fw ?? fr;
    }

    /// <summary>
    /// The application's converter for the value object reads the row's input inside its own spelling ("vo:" and the wrapped
    /// type's JSON) alike under the row's reference handler and under none: "anything a converter of your own writes" is unaffected
    /// (Generators README#json). Each side writes its own document, so Preserve's metadata is its own, and only the value is rewritten.
    /// </summary>
    static string? OwnReadsInput(Row r, Check c, Entry e, Held hv, JsonSerializerOptions o)
    {
        var input = c["input.json"];
        var inner = JsonRow.Inner(e);
        var wrapped = JsonSerializer.Serialize(Make.Unwrap(e, Make.Vo(e, e.Valid())), Make.Held(e.Plain), inner);
        var value = "vo:" + JsonParity.InputFor(input, e, inner, wrapped, "root");
        var bare = new JsonSerializerOptions(o) { ReferenceHandler = null };
        (string? Read, string? Refused, Exception? Depth) Through(JsonSerializerOptions x)
        {
            var (doc, wx) = Jx.Ser(hv, Jx.Full(x));
            if (wx is not null) return (null, "write: " + wx.GetType().Name, Jx.Depth(r, wx) ? wx : null);
            var own = Jx.Tokens(doc!).Where(t => t.Type is JsonTokenType.String or JsonTokenType.PropertyName && t.Text is { } s && s.StartsWith("vo:", StringComparison.Ordinal)).ToList();
            if (own.Count == 0) return (null, null, null);
            var (v, rx) = Jx.De(Jx.Rewrite(doc!, own, _ => JsonSerializer.Serialize(value)), hv.Declared, x);
            if (rx is not null) return (null, "read: " + rx.GetType().Name, Jx.Depth(r, rx) ? rx : null);
            return (JsonSerializer.Serialize(v, hv.Declared, bare), null, null);
        }
        var handled = Through(o); var unhandled = Through(bare);
        if ((handled.Depth ?? unhandled.Depth) is { } depth) { c.Note(NoteKeys.StjMaxdepth, "own converter, read " + input + ": " + Jx.Ex(depth)); return null; }   // Jx.Depth: a text-matching site
        if (handled.Read is null && handled.Refused is null && unhandled.Read is null && unhandled.Refused is null)
        { c.Note(NoteKeys.JsonMemberLeftOutByOptions, "own converter: the options write no member"); return c.Inconclusive("own converter: the options write no member to read"); }
        if (handled.Read != unhandled.Read || handled.Refused != unhandled.Refused)
            return $"read {input} through the application's converter: {handled.Read ?? handled.Refused ?? "no member"} under ReferenceHandler {c["handler"]}, {unhandled.Read ?? unhandled.Refused ?? "no member"} without one";
        c.Note(NoteKeys.Compared, $"read {input} through the application's converter, alike with and without the reference handler: {Jx.Cut(handled.Read ?? handled.Refused)}");
        return null;
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
    public static string? Check(Row r, Check c, Entry e, string input, string text, Held hp, Held hv, JsonSerializerOptions op, JsonSerializerOptions ov)
    {
        var (vp, ep) = Jx.De(text, hp.Declared, op);
        var (vv, ev) = Jx.De(text, hv.Declared, ov);
        var voName = Make.Held(e.Vo).Name;
        string? Named(Exception x) => x is JsonException && x.Message.Contains(voName) && x.InnerException is null ? null
            : $"read {input}: the value object's refusal is {Jx.Ex(x)}{(x.InnerException is null ? "" : " with an inner exception")}, not a JsonException naming {voName}\n  text={Jx.Cut(text)}";
        var stored = c.Is("json.Converters", "StoredJsonConverterFactory");
        if (Jx.Depth(r, ev) || Jx.Depth(r, ep)) { c.Note(NoteKeys.StjMaxdepth, "read " + input + ": " + Jx.Ex((ev ?? ep)!)); return null; }   // Jx.Depth: a text-matching site
        if (ep is null && ev is null)
        {
            if (e.Kind.StartsWith("validated") && input == "refused-by-create" && !stored && e.Refused() is not null && text != Jx.Ser(hp, op).Json && !c.Is("shape.position", "dictionary-key"))
                return $"read {input}: the validated value object read what Create refuses\n  text={Jx.Cut(text)}";
            var sp = JsonSerializer.Serialize(vp, hp.Declared, op); var sv = JsonSerializer.Serialize(vv, hv.Declared, ov);
            if (sp != sv) return $"read {input}: read back differs: wrapped {Jx.Cut(sp)} vs value object {Jx.Cut(sv)}\n  text={Jx.Cut(text)}";
            c.Note(NoteKeys.Compared, "read " + input); return null;
        }
        if (ep is not null && ev is not null)
        {
            // refused alike, message for message: the value object is unaffected, as the twin
            if (ep.GetType() == ev.GetType() && Jx.Swap(ep.Message, Make.Held(e.Plain), Make.Held(e.Vo)) == ev.Message) { c.Note(NoteKeys.JsonReadRefusedByStjAlone, input + ": " + Jx.Ex(ev)); return null; }
            if (Named(ev) is { } f) return f + $"; the wrapped type: {Jx.Ex(ep)}";
            c.Note(NoteKeys.Compared, "read " + input + ": both refuse, the value object naming itself"); return null;
        }
        if (ev is not null)
        {
            var intended = (input == "null" && e.Vo.IsValueType) || (e.Kind.StartsWith("validated") && input == "refused-by-create" && !stored) || (e.Kind == "closed-set" && input != "valid");
            if (!intended) return $"read {input}: the wrapped type read it, the value object threw {Jx.Ex(ev)}\n  text={Jx.Cut(text)}";
            if (Named(ev) is { } f) return f;
            c.Note(NoteKeys.Compared, "read " + input + ": refused as documented"); return null;
        }
        // the wrapped type refused and the value object read: only a JSON null for a sealed record, which reads it as any class does
        if (input == "null" && !e.Vo.IsValueType && e.Plain.IsValueType && Jx.Tokens(JsonSerializer.Serialize(vv, hv.Declared, Jx.Plain)).Any(t => t.Type == JsonTokenType.Null))
        { c.Note(NoteKeys.Compared, "read null: the sealed record reads null, as any class does"); return null; }
        return $"read {input}: the value object read what the wrapped type refused ({Jx.Ex(ep!)})\n  text={Jx.Cut(text)}";
    }
}
