// Shared text surface of the harness: applies a row's text.entry x text.culture x process.converter-lookup to a value object (by reflection over the
// generated members), and the three text claims' checks (L02 no-quote, L03 one-rule, L04 round-trip) which also run the row's JSON half.
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CodoMetis.TypeKit;
namespace Probes.Shapes;

public sealed record Out(bool NotApplicable, bool Ok, object? Value, Exception? Ex, string? Text)
{
    public static Out NA => new(true, false, null, null, null);
}

public static class TextOps
{
    public const string Secret = "S3CR3T-xyz";
    public static object? PI<V>(string s) where V : IParsable<V> => V.Parse(s, null);
    public static object? PS<V>(string s) where V : ISpanParsable<V> => V.Parse(s.AsSpan(), null);
    public static string FS<V>(V v, int size, out bool ok, out int written) where V : ISpanFormattable
    { Span<char> buf = new char[size]; ok = v.TryFormat(buf, out written, default, null); return new string(buf[..written]); }

    public static bool Implements(Type open, Type t) => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == open);
    public static bool Lossy(Entry e) => e.Wrapped is "TimeOnly" or "DateTime" or "DateTimeOffset";
    public static string? Fmt(Entry e) => Lossy(e) ? "O" : null;

    public static void Prepare(Row r, Entry e)
    {
        var c = r["text.culture"];
        try { CultureInfo.CurrentCulture = c switch { "de-DE" => new("de-DE"), "tr-TR" => new("tr-TR"), "ar-SA" => new("ar-SA"), _ => CultureInfo.InvariantCulture }; }
        catch (CultureNotFoundException) { CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; r.Substituted("text.culture", "globalization-invariant", "culture unavailable in this process"); }
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        bool gi; try { gi = new CultureInfo("de-DE").NumberFormat.NumberDecimalSeparator == "."; } catch (CultureNotFoundException) { gi = true; }
        if (c == "globalization-invariant" && !gi) r.Substituted("text.culture", "invariant", "the process is not in globalization-invariant mode");
        if (c != "globalization-invariant" && gi) r.Substituted("text.culture", "globalization-invariant", "the process is in globalization-invariant mode");
        if (r["process.converter-lookup"] == "wrapped-type-converter-looked-up-first") _ = TypeDescriptor.GetConverter(e.Plain);
    }

    static Out Unwrap(Func<Out> f)
    {
        try { return f(); }
        catch (TargetInvocationException t) { return new(false, false, null, t.InnerException, null); }
        catch (Exception ex) { return new(false, false, null, ex, null); }
    }

    /// <summary>Plain text of a wrapped value the way the value object formats it (invariant), used as the sample's text.</summary>
    public static string PlainText(Entry e, object plain) => plain is IFormattable f ? f.ToString(Fmt(e), CultureInfo.InvariantCulture) : plain.ToString()!;

    /// <summary>The value object reads text: a generated Parse(string, IFormatProvider), or a closed set's Parse(string) by name.</summary>
    public static bool HasText(Entry e) => e.Vo.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]) is not null || ByName(e, "Parse", [typeof(string)]) is not null;
    /// <summary>A closed set's own Parse and TryParse, by name and ordinal (Generators README#matching-parsing-and-binding): the provider means nothing to a name.</summary>
    static MethodInfo? ByName(Entry e, string name, Type[] parameters) => e.Kind == "closed-set" ? e.Vo.GetMethod(name, parameters) : null;

    public static Out Read(Entry e, string entry, string text) => Unwrap(() =>
    {
        switch (entry)
        {
            case "Parse":
            {
                var m = e.Vo.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]);
                if (m is not null) return new(false, true, m.Invoke(null, [text, null]), null, null);
                return ByName(e, "Parse", [typeof(string)]) is { } n ? new(false, true, n.Invoke(null, [text]), null, null) : Out.NA;
            }
            case "TryParse":
            {
                var m = e.Vo.GetMethod("TryParse", [typeof(string), typeof(IFormatProvider), e.Vo.MakeByRefType()]);
                if (m is not null) { var a = new object?[] { text, null, null }; var ok = (bool)m.Invoke(null, a)!; return new(false, ok, a[2], null, null); }
                if (ByName(e, "TryParse", [typeof(string), e.Vo.MakeByRefType()]) is not { } n) return Out.NA;
                var b = new object?[] { text, null }; var found = (bool)n.Invoke(null, b)!;
                return new(false, found, b[1], null, null);
            }
            case "IParsable":
            {
                if (!Implements(typeof(IParsable<>), e.Vo)) return Out.NA;
                return new(false, true, typeof(TextOps).GetMethod(nameof(PI))!.MakeGenericMethod(e.Vo).Invoke(null, [text]), null, null);
            }
            case "ISpanParsable":
            {
                if (!Implements(typeof(ISpanParsable<>), e.Vo)) return Out.NA;
                return new(false, true, typeof(TextOps).GetMethod(nameof(PS))!.MakeGenericMethod(e.Vo).Invoke(null, [text]), null, null);
            }
            case "type-converter":
            {
                var c = TypeDescriptor.GetConverter(e.Vo); if (!c.CanConvertFrom(typeof(string))) return Out.NA;
                return new(false, true, c.ConvertFrom(null, null, text), null, null);
            }
            default: return Out.NA;
        }
    });

    /// <summary>The text the entry writes for the value object (null = entry writes no text).</summary>
    public static Out Write(Entry e, string entry, object vo) => Unwrap(() =>
    {
        switch (entry)
        {
            case "type-converter":
            {
                var c = TypeDescriptor.GetConverter(e.Vo); if (!c.CanConvertTo(typeof(string))) return Out.NA;
                return new(false, true, null, null, c.ConvertTo(null, null, vo, typeof(string)) as string);
            }
            case "ToString-format": return vo is IFormattable f ? new(false, true, null, null, f.ToString(Fmt(e), null)) : Out.NA;
            case "TryFormat-short-span":
            {
                if (vo is not ISpanFormattable) return Out.NA;
                var m = typeof(TextOps).GetMethod(nameof(FS))!.MakeGenericMethod(e.Vo);
                var big = new object?[] { vo, 4096, null, null }; var t = (string)m.Invoke(null, big)!;
                return new(false, (bool)big[2]!, null, null, t);
            }
            default: return new(false, true, null, null, Fmt(e) is { } fm && vo is IFormattable g ? g.ToString(fm, null) : vo.ToString());
        }
    });

    public static bool ValidatedKind(Entry e) => e.Kind.StartsWith("validated");

    /// <summary>TryFrom (validated) or From (otherwise) on the wrapped value: Ok + value, or NotApplicable.</summary>
    public static Out TryFrom(Entry e, object plain) => Unwrap(() =>
    {
        if (e.Kind == "closed-set") return Out.NA;
        if (ValidatedKind(e))
        {
            var m = e.Vo.GetMethod("TryFrom", [e.Plain]); if (m is null) return Out.NA;
            var opt = m.Invoke(null, [plain])!; var g = opt.GetType().GetMethod("TryGetValue")!;
            var a = new object?[] { null }; var ok = (bool)g.Invoke(opt, a)!; return new(false, ok, a[0], null, null);
        }
        return new(false, true, Make.Vo(e, plain), null, null);
    });
}

public static class TextClaims
{
    static readonly string[] Entries = ["Parse", "TryParse", "TryFrom", "type-converter", "IParsable", "ISpanParsable", "ToString-format", "TryFormat-short-span"];
    static bool Reads(string entry) => entry is "Parse" or "TryParse" or "IParsable" or "ISpanParsable" or "type-converter";

    static bool WrappedLosesItToo(Entry e, object plain)
    {
        var parse = e.Plain.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]);
        if (parse is null) return false;
        try { return !Equals(parse.Invoke(null, [Convert.ToString(plain), null]), plain); }
        catch (TargetInvocationException) { return true; }
    }

    static bool Same(Entry e, object? a, object? b) => a is not null && b is not null && (a.Equals(b) || (e.Wrapped == "DateTime" && ((DateTime)Make.Unwrap(e, a)).ToUniversalTime() == ((DateTime)Make.Unwrap(e, b)).ToUniversalTime()));

    /// <summary>
    /// Why a generated text entry is missing, where a document says it is not generated: a closed set's surface is its own (Generators
    /// README#closed-sets), no parsing is generated over a type that cannot be parsed, and no span parsing over a type that has none
    /// (#what-is-generated). Each is declared in claims.json too; null where nothing says it is missing.
    /// </summary>
    static string? Missing(Entry e, string entry)
    {
        if (e.Kind == "closed-set") return entry is "TryFrom" or "ISpanParsable" or "ToString-format" or "TryFormat-short-span" ? "closed-set" : null;
        if (!TextOps.HasText(e) && entry is not ("ToString-format" or "TryFormat-short-span" or "TryFrom")) return "not-parsable";
        if (entry == "ISpanParsable" && e.Wrapped is "Uri" or "enum" or "flags-enum" or "NodaTime.Instant") return "no-span-parsing";
        return null;
    }
    /// <summary>The check does not apply: the entry <see cref="Missing"/> names is not generated, cited by the sentence that says so.</summary>
    static string? NotGenerated(Check c, Entry e, string entry) => Missing(e, entry) switch
    {
        "closed-set" => c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#closed-sets"), entry),
        _ => c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#what-is-generated"), entry),
    };

    // ---- L04: write then read gives back the value; text half and JSON half
    /// <summary>
    /// L04's checks: the generated ToString() is the wrapped type's invariant text whatever the culture (invariant-whatever-the-culture);
    /// the row's writing entry writes the same (the-type-converter-writes-the-same); that text, or TryFrom's value, reads back as the same
    /// value wherever the wrapped type's own does (text-reads-back); and the JSON half, the value object's own document read back and the
    /// row's changed input refused with a JsonException naming the value object (json-refusal-is-a-json-exception). Every check runs;
    /// returns the first failure.
    /// </summary>
    public static string? RoundTrip(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid(); var vo = Make.Vo(e, plain);
        var fails = new List<string?>();
        if (entry == "TryFrom")
            fails.Add(r.Check("text-reads-back", Baseline.RoundTrip, c =>
            {
                var t = TextOps.TryFrom(e, plain);
                if (t.NotApplicable) return Missing(e, entry) is not null ? NotGenerated(c, e, entry) : "TryFrom is not generated for the value object";
                if (!t.Ok || !Same(e, t.Value, vo)) return $"TryFrom(valid) gave ok={t.Ok} value={t.Value}, expected {vo}";
                c.Note(NoteKeys.Compared, "TryFrom(valid)");
                return null;
            }));
        else
        {
            var invariant = TextOps.PlainText(e, plain);
            var w0 = TextOps.Write(e, "ToString", vo);
            fails.Add(r.Check("invariant-whatever-the-culture", Baseline.Twin, c =>
            {
                if (w0.Ex is not null) return $"ToString(): threw {w0.Ex.GetType().Name}: {w0.Ex.Message}";
                if (w0.Text != invariant) return $"ToString(): text '{w0.Text}' differs from the wrapped type's invariant text '{invariant}' under culture {c["text.culture"]}";
                c.Note(NoteKeys.Compared, $"ToString() under {c["text.culture"]}");
                return null;
            }));
            var writes = entry is "ToString-format" or "TryFormat-short-span" or "type-converter";
            var w = writes ? TextOps.Write(e, entry, vo) : w0;
            if (writes)
                fails.Add(r.Check("the-type-converter-writes-the-same", Baseline.Twin, c =>
                {
                    if (w.NotApplicable)
                    {
                        if (Missing(e, entry) is not null) return NotGenerated(c, e, entry);
                        // TryFormat is generated where the wrapped type has it (ValueObjectFormattableAspect); the wrapped type has none either
                        if (entry == "TryFormat-short-span" && !typeof(ISpanFormattable).IsAssignableFrom(Make.Held(e.Plain)))
                            return c.Inconclusive($"no TryFormat: the wrapped {Make.Held(e.Plain).Name} has no ISpanFormattable either");
                        return $"{entry}: the value object writes no text where the wrapped {Make.Held(e.Plain).Name} does";
                    }
                    if (w.Ex is not null) return $"{entry}: writing text threw {w.Ex.GetType().Name}: {w.Ex.Message}";
                    var text = w.Text!;
                    var wantText = entry is "TryFormat-short-span" or "type-converter" ? (plain is IFormattable pf ? pf.ToString(null, CultureInfo.InvariantCulture) : plain.ToString()!) : invariant;
                    if (text != wantText) return $"{entry}: text '{text}' differs from the wrapped type's invariant text '{wantText}' under culture {c["text.culture"]}";
                    if (entry == "TryFormat-short-span")
                    {
                        var args = new object?[] { vo, 1, null, null };
                        try
                        {
                            typeof(TextOps).GetMethod(nameof(TextOps.FS))!.MakeGenericMethod(e.Vo).Invoke(null, args);
                            if (text.Length > 1 && (bool)args[2]!) return $"TryFormat into a 1-char span said it fit text of {text.Length} chars";
                            if (text.Length > 1 && (int)args[3]! != 0) return "TryFormat wrote chars into a too-short span and still returned false";
                        }
                        catch (TargetInvocationException t) { return $"TryFormat into a short span threw {t.InnerException!.GetType().Name}"; }
                    }
                    c.Note(NoteKeys.Compared, $"{entry} under {c["text.culture"]}");
                    return null;
                }));
            fails.Add(r.Check("text-reads-back", Baseline.RoundTrip, c =>
            {
                var readEntry = entry is "ToString-format" or "TryFormat-short-span" ? "Parse" : entry;
                if (Missing(e, entry) is not null) return NotGenerated(c, e, entry);
                if (Missing(e, readEntry) is not null) return NotGenerated(c, e, readEntry);
                if (w.NotApplicable || w.Ex is not null || w.Text is null) return c.Inconclusive($"{entry}: no text written to read back, judged by the-type-converter-writes-the-same");
                // claims.json declares it: not-promised dates-and-times-lose-detail-as-text
                if (entry == "type-converter" && TextOps.Lossy(e)) return c.Inapplicable(Cite.NotPromised("dates-and-times-lose-detail-as-text"), entry);
                var text = w.Text;
                var back = TextOps.Read(e, readEntry, text);
                if (back.NotApplicable) return $"{readEntry}: the value object has no {readEntry} to read its own text '{text}' back";
                if (back.Ex is not null) return $"{readEntry}: reading back '{text}' threw {back.Ex.GetType().Name}: {back.Ex.Message}";
                if (!back.Ok) return $"{readEntry}: could not read back its own text '{text}'";
                if (!Same(e, back.Value, vo))
                {
                    // Differential: the value object only has to round-trip where its wrapped type does
                    // (TimeOnly.ToString() under the invariant culture prints HH:mm and drops the seconds).
                    if (!WrappedLosesItToo(e, plain)) return $"{readEntry}: '{text}' read back as {back.Value}, expected {vo}";
                    if (TextOps.Lossy(e)) return c.Inapplicable(Cite.NotPromised("dates-and-times-lose-detail-as-text"), $"{readEntry}: the wrapped type's own text does not read back either");
                    return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#parsing-and-formatting"), $"{readEntry}: the wrapped type's own text does not read back either");   // "wherever the underlying type's own does"
                }
                c.Note(NoteKeys.Compared, $"{readEntry} read back '{text}'");
                return null;
            }));
        }
        fails.AddRange(JsonRoundTrip(r, e));
        return fails.FirstOrDefault(f => f is not null);
    }

    /// <summary>
    /// L04's JSON half, json-refusal-is-a-json-exception: the value object's own document reads back as written (the round trip the
    /// refusal is measured from), and the row's changed input is read or refused with a JsonException, at the root naming the value object
    /// and carrying no inner exception ("Input it can't read throws a JsonException naming the value object and the underlying type").
    /// </summary>
    static List<string?> JsonRoundTrip(Row r, Entry e)
    {
        const string Obligation = "json-refusal-is-a-json-exception";
        var o = JsonRow.Apply(r, e).Options!;
        var pos = r["shape.position"]; var con = r.Get("shape.containing") ?? "class";
        var plain = e.Valid(); var vo = Make.Vo(e, plain);
        var hv = Pos.Build(pos, con, e, true, vo); var hp = Pos.Build(pos, con, e, false, plain);
        string? jv = null; Exception? wx = null;
        try { jv = JsonSerializer.Serialize(hv.Value, hv.Declared, o); } catch (Exception ex) { wx = ex; }
        string? Back(Held h, string text, out Exception? ex)
        {
            ex = null;
            try { var v = JsonSerializer.Deserialize(text, h.Declared, o); return v is null ? "null" : JsonSerializer.Serialize(v, h.Declared, o); } catch (Exception x) { ex = x; return null; }
        }
        bool Guarded(Exception x, Check c) => c.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && x is NotSupportedException;
        var fails = new List<string?>();
        fails.Add(r.Check(Obligation, Baseline.RoundTrip, c =>
        {
            if (wx is not null)
            {
                try { JsonSerializer.Serialize(hp.Value, hp.Declared, o); }
                catch (Exception px) { return c.Inconclusive("write: the options refuse the shape for the wrapped type too: " + px.GetType().Name); }
                // Text-matching site, kept in this step (WS2 step 4): the depth refusal is told by the message's words. Baseline gap: no twin in
                // the row shows System.Text.Json charging a converter a level, and no record or fact cites it (the three-sided differential).
                if (c.Is("json.MaxDepth", "3") && wx.Message.Contains("maximum allowed depth")) { c.Note(NoteKeys.StjMaxdepth, "write"); return null; }
                if (Guarded(wx, c)) return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "write");
                return $"serializing threw {wx.GetType().Name} where the wrapped type did not: {wx.Message}";
            }
            var b = Back(hv, jv!, out var ex1);
            if (ex1 is not null)
            {
                if (Guarded(ex1, c)) return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "read back");
                // Text-matching site, kept in this step (WS2 step 4), as above: the depth refusal told by its words, no twin showing it.
                if (c.Is("json.MaxDepth", "3") && ex1.Message.Contains("maximum allowed depth")) { c.Note(NoteKeys.StjMaxdepth, "read back"); return null; }
                // the wrapped twin does not read its own document back under these options either: nothing to compare
                if (PlainFails(hp, o) is { } px) { return c.Inconclusive($"read back: the wrapped type does not read its own document back under these options either: {px.GetType().Name}: {Cut(px.Message)}"); }
                return $"own JSON {Cut(jv)} did not read back: {ex1.GetType().Name}: {Cut(ex1.Message)}";
            }
            if (b != jv)
            {
                // under Preserve the serializer numbers references per call: the twin's document read back and written again, alike
                if (c.Is("json.ReferenceHandler", "Preserve") && TwinRoundTrip(hp, o) is { } tw && tw.Back != tw.Json)
                    return c.Inconclusive($"JSON round trip: written again it differs, for the wrapped type alike ({Cut(tw.Json)} / {Cut(tw.Back)})");
                return $"JSON round trip differs: wrote {Cut(jv)}, read and wrote {Cut(b)}";
            }
            c.Note(NoteKeys.Compared, "JSON round trip");
            return null;
        }));
        // a changed input must be read or refused with a JsonException (never another type)
        var input = r["input.json"];
        if (input != "valid" && jv is not null)
            fails.Add(r.Check(Obligation, Baseline.Refusal, c =>
            {
                // "Your own converter wins": the application's converter reads the value object, so what it throws is its own. It shares the
                // obligation with the round trip above, which runs under it, so it is decided here, not declared in claims.json.
                if (c.Is("json.Converters", "own-for-value-object")) return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), "input " + input);
                var text = JsonParity.InputFor(input, e, o, jv, pos);
                if (text == jv) return c.Inconclusive($"input {input}: no place for it in {Cut(jv)}");
                Back(hv, text, out var ex2);
                if (ex2 is not null and not JsonException)
                {
                    if (Guarded(ex2, c)) return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "input " + input);
                    // the serializer's own refusal of the document, for the wrapped twin alike
                    Back(hp, text, out var px);
                    if (px is not null && px.GetType() == ex2.GetType()) return c.Inconclusive($"input {input}: {ex2.GetType().Name} for the wrapped type alike: {Cut(px.Message)}");
                    return $"input {input}: {ex2.GetType().Name} instead of JsonException: {Cut(ex2.Message)}"
                        + (ex2.InnerException is { } inner ? $" / inner {inner.GetType().FullName}: {Cut(inner.Message)} @ {Cut(inner.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "")}" : "");
                }
                if (RootRefusal(e, o, input) is { } refusal) return refusal;
                c.Note(NoteKeys.Compared, $"input {input}: {(ex2 is null ? "read" : "a JsonException")}");
                return null;
            }));
        return fails;
    }

    /// <summary>
    /// The changed input read at the root, where no container stands between the serializer and the generated converter: a
    /// JsonException the value object throws there names it and carries no inner exception, "Input it can't read throws a
    /// JsonException naming the value object and the underlying type" (Generators README#json).
    /// </summary>
    static string? RootRefusal(Entry e, JsonSerializerOptions o, string input)
    {
        var vt = Make.Held(e.Vo);
        string root;
        try { root = JsonSerializer.Serialize(Make.Vo(e, e.Valid()), vt, o); } catch { return null; }   // a root the options refuse to write is judged above
        var text = JsonParity.InputFor(input, e, o, root, "root");
        if (text == root) return null;
        try { JsonSerializer.Deserialize(text, vt, o); return null; }
        catch (JsonException x)
        {
            if (x.InnerException is null && x.Message.Contains(vt.Name, StringComparison.Ordinal)) return null;
            return $"input {input} at the root: the JsonException {(x.InnerException is { } i ? "carries an inner " + i.GetType().Name : "does not name " + vt.Name)}: {Cut(x.Message)}\n  text={Cut(text)}";
        }
        catch (Exception) { return null; }   // another exception type is judged above, at the row's position
    }
    /// <summary>The wrapped twin's refusal to read back its own document under the options, or null where it reads it back.</summary>
    static Exception? PlainFails(Held hp, JsonSerializerOptions o)
    {
        try { var jp = JsonSerializer.Serialize(hp.Value, hp.Declared, o); JsonSerializer.Deserialize(jp, hp.Declared, o); return null; } catch (Exception x) { return x; }
    }
    /// <summary>The wrapped twin's document and that document read back and written again, or null where either throws.</summary>
    static (string Json, string Back)? TwinRoundTrip(Held hp, JsonSerializerOptions o)
    {
        try { var jp = JsonSerializer.Serialize(hp.Value, hp.Declared, o); var v = JsonSerializer.Deserialize(jp, hp.Declared, o); return (jp, v is null ? "null" : JsonSerializer.Serialize(v, hp.Declared, o)); }
        catch { return null; }
    }
    static bool PlainHasInner(string text, Held hp, JsonSerializerOptions o)
    { try { JsonSerializer.Deserialize(text, hp.Declared, o); return false; } catch (Exception x) { return x.InnerException is not null; } } // STJ's own inner (the container, a dictionary value) is not the value object's
    internal static string Cut(string? s) => s is null ? "(null)" : s.Length > 160 ? s[..160] + "..." : s;

    // ---- L02: a rejection never repeats the input
    // Text-matching site, kept in this step (WS2 step 4): System.Text.Json's Path, which holds a rejected dictionary key (not-promised
    // a-rejected-dictionary-key-in-the-path), is cut off by its words. Baseline gap: no twin in the row shows the wrapped type's key in the Path too.
    static string StripPath(Exception x) => x.Message.IndexOf(" Path:", StringComparison.Ordinal) is var i and >= 0 ? x.Message[..i] : x.Message;

    /// <summary>
    /// L02's checks: a parsing entry's refusal of a secret, or of the text Create refuses, neither quotes it (parsing-never-repeats, a
    /// FormatException where it is not the type converter's) nor carries an inner exception that does (no-inner-that-quotes); and a JSON
    /// read of the secret, or of the row's changed input, where the wrapped value stands in the document refuses without quoting it, but
    /// for the dictionary key System.Text.Json puts in the Path (json-never-repeats). Every check runs; returns the first failure.
    /// </summary>
    public static string? NoQuote(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid();
        var texts = new List<string> { TextOps.Secret };
        if (TextOps.ValidatedKind(e) && e.Refused() is { } rf) texts.Add(TextOps.PlainText(e, rf));
        var fails = new List<string?>();
        if (Reads(entry))
        {
            var outs = texts.Select(t => (Text: t, Out: TextOps.Read(e, entry, t))).ToList();
            string? Absent(Check c) => Missing(e, entry) is not null ? NotGenerated(c, e, entry) : $"{entry}: the value object does not read text through it";
            fails.Add(r.Check("parsing-never-repeats", Baseline.Refusal, c =>
            {
                if (outs.Any(x => x.Out.NotApplicable)) return Absent(c);
                var refused = 0;
                foreach (var (t, o) in outs)
                {
                    if (o.Ex is null) { if (!o.Ok) refused++; continue; }   // TryParse answers false: a refusal with nothing to quote
                    refused++;
                    if (entry == "TryParse") return $"TryParse threw {o.Ex.GetType().Name} on '{t}': {Cut(o.Ex.Message)}";
                    if (o.Ex.Message.Contains(t)) return $"{entry}: {o.Ex.GetType().Name} quotes the input: {Cut(o.Ex.Message)}";
                    if (entry != "type-converter" && o.Ex is not FormatException) return $"{entry}: refusal of '{t}' is {o.Ex.GetType().Name}, not FormatException: {Cut(o.Ex.Message)}";
                }
                if (refused == 0) return c.Inconclusive($"{entry}: every text read, none refused: nothing to quote");
                c.Note(NoteKeys.Compared, $"{entry}: {refused} refused without the text");
                return null;
            }));
            fails.Add(r.Check("no-inner-that-quotes", Baseline.Refusal, c =>
            {
                if (outs.Any(x => x.Out.NotApplicable)) return Absent(c);
                var thrown = 0;
                foreach (var (t, o) in outs)
                {
                    if (o.Ex is null) continue;
                    thrown++;
                    if (o.Ex.InnerException is { } inner && inner.Message.Contains(t)) return $"{entry}: inner {inner.GetType().Name} quotes the input";
                    if (o.Ex.InnerException is not null && entry != "type-converter") return $"{entry}: refusal of '{t}' carries an inner {o.Ex.InnerException.GetType().Name}";
                    if (entry == "type-converter" && o.Ex.InnerException is not null) c.Note(NoteKeys.ConverterWrapsInner);
                }
                if (thrown == 0) return c.Inconclusive($"{entry}: no text refused with an exception: no inner to look at");
                c.Note(NoteKeys.Compared, $"{entry}: {thrown} refusals without a quoting inner");
                return null;
            }));
        }
        // JSON half
        var opts = JsonRow.Apply(r, e).Options!;
        var pos = r["shape.position"]; var con = r.Get("shape.containing") ?? "class";
        var hp = Pos.Build(pos, con, e, false, plain); var hv = Pos.Build(pos, con, e, true, Make.Vo(e, plain));
        var input = r["input.json"];
        fails.Add(r.Check("json-never-repeats", Baseline.Refusal, c =>
        {
            // the application's own converter owns the message; claims.json declares it
            if (c.Get("json.Converters") is "own-for-wrapped" or "own-for-value-object") return c.Inapplicable(Cite.NotPromised("the-applications-own-converter"));
            string? doc = null, sample;
            try { doc = JsonSerializer.Serialize(hv.Value, hv.Declared, opts); }
            catch (Exception vx)
            {
                // the value object's document is the wrapped type's (L01): where it cannot be written, the twin's places the input
                try { doc = JsonSerializer.Serialize(hp.Value, hp.Declared, opts); }
                catch (Exception px) { return c.Inconclusive($"the options refuse the shape, for the wrapped type too: {vx.GetType().Name} / {px.GetType().Name}"); }
            }
            try { sample = JsonSerializer.Serialize(Make.Unwrap(e, Make.Vo(e, plain)), e.Plain, opts); }
            catch (Exception sx) { return c.Inconclusive("the wrapped value alone is not written under these options: " + sx.GetType().Name); }
            var at = doc.IndexOf(sample, StringComparison.Ordinal);
            var variants = new List<(string Text, string Needle)>();
            if (at >= 0) variants.Add((doc[..at] + "\"" + TextOps.Secret + "\"" + doc[(at + sample.Length)..], TextOps.Secret));
            else if (pos == "dictionary-key" && Probes.Json.Jx.Tokens(doc).FirstOrDefault(t => t.Type == JsonTokenType.PropertyName && t.Text == sample.Trim('"')) is { } key)
                // a key is a property name, the wrapped value's text without its quotes: the secret goes there
                variants.Add((Probes.Json.Jx.Rewrite(doc, [key], _ => "\"" + TextOps.Secret + "\""), TextOps.Secret));
            if (input != "valid" && at >= 0)
            {
                var text = JsonParity.InputFor(input, e, opts, doc, pos);
                var x = text.Length - doc.Length + sample.Length;
                if (x > 0 && text != doc) { var lit = text.Substring(at, x).Trim('"'); if (lit.Length >= 6 && lit.Length < 60 && lit.All(ch => !char.IsPunctuation(ch) || ch is '-' or ':' or '.')) variants.Add((text, lit)); }
            }
            if (variants.Count == 0) return c.Inconclusive($"the wrapped value has no place in {Cut(doc)}: the options leave it out");
            var rejected = 0;
            foreach (var (text, needle) in variants)
            {
                try { JsonSerializer.Deserialize(text, hv.Declared, opts); }
                catch (Exception ex)
                {
                    rejected++;
                    // the Path is System.Text.Json's (not-promised a-rejected-dictionary-key-in-the-path): a JsonException's message is read without it
                    var msg = ex is JsonException ? StripPath(ex) : ex.Message;
                    if (msg.Contains(needle)) return $"JSON read: {ex.GetType().Name} quotes the input: {Cut(ex.Message)}" + (pos == "dictionary-key" ? " [dictionary-key]" : "");
                    if (ex.InnerException is { } inner && inner.Message.Contains(needle)) return $"JSON read: inner {inner.GetType().Name} quotes the input";
                    if (ex.InnerException is not null && ex is JsonException && !(ex.InnerException is JsonException) && !PlainHasInner(text, hp, opts)) return $"JSON read: refusal carries inner {ex.InnerException.GetType().Name}: {Cut(ex.InnerException.Message)}";
                }
            }
            if (rejected == 0) return c.Inconclusive("every JSON input read, none refused: nothing to quote");
            c.Note(NoteKeys.Compared, $"JSON read: {rejected} refused without the input");
            return null;
        }));
        return fails.FirstOrDefault(f => f is not null);
    }

    // ---- L03: one set of rules: every entry runs Create and reports its fault
    /// <summary>
    /// L03's checks: TryFrom (tryfrom-runs-create) and the parsing entries (parsing-runs-create) refuse the value Create refuses and accept
    /// a valid one, the JSON converter reads back what it writes and refuses what Create refuses (json-runs-create), and every refusal,
    /// FromKnownGood's, the FormatException and the JsonException, names the fault (refusals-name-the-fault). A value object without Create
    /// is held to accepting the valid value. Every check runs; returns the first failure.
    /// </summary>
    public static string? OneRule(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid();
        var fails = new List<string?>();
        var validated = TextOps.ValidatedKind(e) && e.Refused() is not null;
        if (!validated)
        {
            // no Create: the valid value passes every entry
            if (entry == "TryFrom")
                fails.Add(r.Check("tryfrom-runs-create", Baseline.RoundTrip, c =>
                {
                    var t = TextOps.TryFrom(e, plain);
                    if (t.NotApplicable) return Missing(e, entry) is not null ? NotGenerated(c, e, entry) : "TryFrom is not generated for the value object";
                    if (!t.Ok || t.Ex is not null) return "TryFrom(valid) refused on a value object with no rule";
                    c.Note(NoteKeys.Compared, "TryFrom(valid), no rule"); return null;
                }));
            else if (Reads(entry))
                fails.Add(r.Check("parsing-runs-create", Baseline.RoundTrip, c =>
                {
                    if (Missing(e, entry) is not null) return NotGenerated(c, e, entry);
                    var o = TextOps.Read(e, entry, TextOps.PlainText(e, plain));
                    if (o.NotApplicable) return $"{entry}: the value object does not read text through it";
                    if (o.Ex is not null || !o.Ok) return $"{entry}: valid text refused: {o.Ex?.GetType().Name} {Cut(o.Ex?.Message)}";
                    c.Note(NoteKeys.Compared, $"{entry}(valid), no rule"); return null;
                }));
            fails.AddRange(JsonOneRule(r, e, false));
            return fails.FirstOrDefault(f => f is not null);
        }
        var refusedValue = e.Refused()!;
        var rtext = TextOps.PlainText(e, refusedValue);
        if (entry == "TryFrom")
        {
            fails.Add(r.Check("tryfrom-runs-create", Baseline.Refusal, c =>
            {
                var t = TextOps.TryFrom(e, refusedValue); if (t.Ex is not null) return "TryFrom threw " + t.Ex.GetType().Name; if (t.Ok) return "TryFrom accepted the value Create refuses";
                c.Note(NoteKeys.Compared, "TryFrom refused"); return null;
            }));
            fails.Add(r.Check("refusals-name-the-fault", Baseline.Refusal, c =>
            {
                var kg = Unwrap(() => e.Vo.GetMethod("FromKnownGood")!.Invoke(null, [refusedValue, null]));
                if (kg is not { } kex) return "FromKnownGood accepted the value Create refuses"; if (!kex.Message.Contains("Refused")) return "FromKnownGood does not name the fault: " + Cut(kex.Message);
                c.Note(NoteKeys.Compared, "FromKnownGood names the fault"); return null;
            }));
        }
        else if (Reads(entry))
        {
            var o = Missing(e, entry) is null ? TextOps.Read(e, entry, rtext) : Out.NA;
            fails.Add(r.Check("parsing-runs-create", Baseline.Refusal, c =>
            {
                if (Missing(e, entry) is not null) return NotGenerated(c, e, entry);
                if (o.NotApplicable) return $"{entry}: the value object does not read text through it";
                if (entry == "TryParse") { if (o.Ex is not null) return "TryParse threw " + o.Ex.GetType().Name; if (o.Ok) return "TryParse accepted text Create refuses"; }
                else if (o.Ex is null) return $"{entry} accepted '{rtext}' that Create refuses";
                c.Note(NoteKeys.Compared, $"{entry} refused"); return null;
            }));
            if (entry != "TryParse")
                fails.Add(r.Check("refusals-name-the-fault", Baseline.Refusal, c =>
                {
                    if (Missing(e, entry) is not null) return NotGenerated(c, e, entry);
                    if (o.NotApplicable || o.Ex is null) return c.Inconclusive($"{entry}: no refusal to name the fault, judged by parsing-runs-create");
                    if (!o.Ex.Message.Contains("Refused")) return $"{entry}: refusal does not name the fault ({o.Ex.GetType().Name}): {Cut(o.Ex.Message)}";
                    if (entry != "type-converter" && o.Ex is not FormatException) return $"{entry}: refusal is {o.Ex.GetType().Name}, not FormatException";
                    c.Note(NoteKeys.Compared, $"{entry}: the {o.Ex.GetType().Name} names the fault"); return null;
                }));
        }
        fails.AddRange(JsonOneRule(r, e, true));
        return fails.FirstOrDefault(f => f is not null);
    }
    static Exception? Unwrap(Func<object?> f) { try { f(); return null; } catch (TargetInvocationException t) { return t.InnerException; } catch (Exception ex) { return ex; } }

    /// <summary>
    /// The JSON half of "one set of rules", on every row under the row's options: the value object reads back what it writes, and a
    /// validated one refuses the value Create refuses, where the wrapped type reads it, with a JsonException naming the fault. The two
    /// named paths are checked as what they are: an application's own converter for the value object takes precedence (it reads its
    /// own spelling back), and StoredJsonConverterFactory reads the refused value without validation (README, "Only two paths skip
    /// validation"). A depth limit is the framework's only where a converter the application registers for the wrapped type, at the
    /// same position, is refused alike (design note D1: System.Text.Json counts a custom converter's value one level deeper).
    /// </summary>
    static List<string?> JsonOneRule(Row r, Entry e, bool validated)
    {
        const string Obligation = "json-runs-create";
        var o = JsonRow.Apply(r, e).Options!;
        var pos = r["shape.position"]; var con = r.Get("shape.containing") ?? "class"; var plain = e.Valid();
        var hp = Pos.Build(pos, con, e, false, plain); var hv = Pos.Build(pos, con, e, true, Make.Vo(e, plain));
        static (string? Json, Exception? Ex) Write(Held h, JsonSerializerOptions x) { try { return (JsonSerializer.Serialize(h.Value, h.Declared, x), null); } catch (Exception ex) { return (null, ex); } }
        static (object? Value, Exception? Ex) Read(string text, Held h, JsonSerializerOptions x) { try { return (JsonSerializer.Deserialize(text, h.Declared, x), null); } catch (Exception ex) { return (null, ex); } }
        bool OwnConverterRefusedToo(Exception voEx)
        {
            var own = new JsonSerializerOptions(o);
            own.Converters.Insert(0, (System.Text.Json.Serialization.JsonConverter)Activator.CreateInstance(typeof(OwnWrappedConverter<>).MakeGenericType(Make.Held(e.Plain)), JsonRow.Inner(e))!);
            var (text, wex) = Write(hp, own);
            var ex = wex ?? Read(text!, hp, own).Ex;
            return ex is not null && ex.GetType() == voEx.GetType();
        }
        // Where the wrapped type alone refuses under the row's options, checked in the same row with the value object refusing alike,
        // the JSON half has nothing to compare and the row is judged by its text entries (as decision 99 judges a document ASP.NET alone refuses).
        // A pass, as decision 99's document refused by ASP.NET alone is: the refusal is the framework's, shown by the twin in the same row.
        string? Alone(Check c, string what) { c.Note(NoteKeys.JsonHalfWrappedAloneRefuses, what); return null; }
        bool Guarded(Exception x) => r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && x is NotSupportedException;
        var (jp, xp) = Write(hp, o); var (jv, xv) = Write(hv, o);
        var fails = new List<string?>();
        // Why the value object's own document has nothing to compare under the row's options, or null where it reads back: the refused
        // value is then not told apart from a document the options do not let it read, and step 2 has nothing to check either.
        string? nothing = null;
        // 1. the valid value reads back as written
        fails.Add(r.Check(Obligation, Baseline.RoundTrip, c =>
        {
            string? None(string why) { nothing = why; return Alone(c, why); }
            if (xp is not null && xv is not null) return None("write");
            if (xp is not null) { nothing = "the wrapped type is not written"; return c.Inconclusive("JSON write: the wrapped type is not written under these options, the value object is: JSON parity (L01) judges it"); }
            if (xv is not null)
            {
                if (Guarded(xv)) { nothing = "the Preserve guard refuses the value object"; return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "write"); }
                if (OwnConverterRefusedToo(xv)) return None("write, with a converter of the application's for the wrapped type");
                return $"JSON: the value object is not written where the wrapped type is ({xv.GetType().Name}: {Cut(xv.Message)})";
            }
            var (back, xb) = Read(jv!, hv, o);
            if (xb is not null)
            {
                if (OwnConverterRefusedToo(xb)) return None("read back, with a converter of the application's for the wrapped type");
                if (Read(jp!, hp, o).Ex is not null) return None("read back");
                return $"JSON: the value object does not read back what it writes ({xb.GetType().Name}: {Cut(xb.Message)}): {Cut(jv)}";
            }
            var again = Write(new Held(hv.Declared, back!), o).Json;
            if (again != jv) return $"JSON: read back as {Cut(again)}, written as {Cut(jv)}";
            if (c.Is("json.Converters", "own-for-value-object")) c.Note(NoteKeys.OwnVo, "the application's converter takes precedence and reads its own spelling");
            else c.Note(NoteKeys.Compared, "JSON read back");
            return null;
        }));
        if (!validated || jp is null) return fails;
        // 2. the value Create refuses, placed where the wrapped value stands in the wrapped type's document
        var text = JsonParity.InputFor("refused-by-create", e, o, jp, pos);
        var (accepted, xr) = Read(text, hv, o);
        var twinReads = Read(text, hp, o).Ex is null;   // the wrapped type reads the refused value's document: only then is a refusal Create's
        var stored = r.Is("json.Converters", "StoredJsonConverterFactory");
        fails.Add(r.Check(Obligation, stored ? Baseline.Docs("README.md#what-typekit-guarantees") : Baseline.Refusal, c =>
        {
            // "Your own converter wins": the generated converter does not run, so no Create; the round trip above runs under it, which
            // shares the obligation, so it is decided here, not declared in claims.json
            if (c.Is("json.Converters", "own-for-value-object")) return c.Inapplicable(Cite.Docs("src/CodoMetis.TypeKit.AspNetCore/README.md#what-the-document-says"), "the refused value");
            if (xr is not null && Guarded(xr)) return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "the refused value");
            if (nothing is not null) return c.Inconclusive($"the refused value: the value object's own document has nothing to compare under these options ({nothing})");
            if (text == jp) return c.Inconclusive($"the refused value has no place in {Cut(jp)} at {pos}");
            if (!twinReads)
            {
                if (xr is null) return $"JSON read accepted text the wrapped type refuses: {Cut(text)}";
                return Alone(c, "the refused value's JSON");
            }
            if (stored)
            {
                // "Only two paths skip validation, and both are named": StoredJsonConverterFactory reads the stored value without Create
                if (xr is not null) return $"StoredJsonConverterFactory refused a value Create refuses ({xr.GetType().Name}): it reads stored JSON without validation";
                c.Note(NoteKeys.StoredFactoryReadUnvalidated); return null;   // not-promised stored-json-skips-validation
            }
            if (xr is null) return $"JSON read accepted the value Create refuses: {Cut(text)}";
            if (xr is not JsonException)
            {
                if (Guarded(xr)) return c.Refused(Cite.Docs("src/CodoMetis.TypeKit.Generators/README.md#json"), "the refused value");
                return $"JSON refusal is {xr.GetType().Name}, not JsonException: {Cut(xr.Message)}";
            }
            c.Note(NoteKeys.Compared, "the refused value refused");
            return null;
        }));
        if (xr is JsonException && twinReads && !stored && nothing is null && !r.Is("json.Converters", "own-for-value-object") && text != jp)
            fails.Add(r.Check("refusals-name-the-fault", Baseline.Refusal, c =>
            {
                if (xr.Message.Contains("Refused")) { c.Note(NoteKeys.Compared, "the JsonException names the fault"); return null; }
                if (OwnConverterRefusedToo(xr)) return Alone(c, "refused read, with a converter of the application's for the wrapped type");
                return $"JsonException does not name the fault: {Cut(xr.Message)}\n  text={Cut(text)} (the wrapped type reads it; its own document {Cut(jp)})";
            }));
        return fails;
    }
}
