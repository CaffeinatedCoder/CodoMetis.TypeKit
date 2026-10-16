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

    public static bool HasText(Entry e) => e.Vo.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]) is not null;

    public static Out Read(Entry e, string entry, string text) => Unwrap(() =>
    {
        switch (entry)
        {
            case "Parse":
            {
                var m = e.Vo.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]); if (m is null) return Out.NA;
                return new(false, true, m.Invoke(null, [text, null]), null, null);
            }
            case "TryParse":
            {
                var m = e.Vo.GetMethod("TryParse", [typeof(string), typeof(IFormatProvider), e.Vo.MakeByRefType()]); if (m is null) return Out.NA;
                var a = new object?[] { text, null, null }; var ok = (bool)m.Invoke(null, a)!;
                return new(false, ok, a[2], null, null);
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

    static bool WrappedLosesItToo(Entry e, object plain)
    {
        var parse = e.Plain.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]);
        if (parse is null) return false;
        try { return !Equals(parse.Invoke(null, [Convert.ToString(plain), null]), plain); }
        catch (TargetInvocationException) { return true; }
    }

    static bool Same(Entry e, object? a, object? b) => a is not null && b is not null && (a.Equals(b) || (e.Wrapped == "DateTime" && ((DateTime)Make.Unwrap(e, a)).ToUniversalTime() == ((DateTime)Make.Unwrap(e, b)).ToUniversalTime()));

    // ---- L04: write then read gives back the value; text half and JSON half
    public static string? RoundTrip(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid(); var vo = Make.Vo(e, plain);
        if (entry == "TryFrom")
        {
            var t = TextOps.TryFrom(e, plain);
            if (t.NotApplicable) r.Note("entry-not-applicable", "TryFrom"); else if (!t.Ok || !Same(e, t.Value, vo)) return $"TryFrom(valid) gave ok={t.Ok} value={t.Value}, expected {vo}";
        }
        else if (!TextOps.HasText(e)) r.Note("no-text", e.Kind);
        else
        {
            var w = TextOps.Write(e, entry is "Parse" or "TryParse" or "IParsable" or "ISpanParsable" ? "ToString" : entry, vo);
            if (w.NotApplicable) r.Note("entry-not-applicable", entry);
            else if (w.Ex is not null) return $"{entry}: writing text threw {w.Ex.GetType().Name}: {w.Ex.Message}";
            else
            {
                var text = w.Text!;
                var wantText = entry is "TryFormat-short-span" or "type-converter" ? (plain is IFormattable pf ? pf.ToString(null, CultureInfo.InvariantCulture) : plain.ToString()!) : TextOps.PlainText(e, plain);
                if (e.Kind != "closed-set" && entry is "ToString-format" or "TryFormat-short-span" or "type-converter" && text != wantText) return $"{entry}: text '{text}' differs from the wrapped type's invariant text '{wantText}' under culture {r["text.culture"]}";
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
                var readEntry = entry is "ToString-format" or "TryFormat-short-span" ? "Parse" : entry;
                if (!(entry == "type-converter" && TextOps.Lossy(e)))
                {
                    var back = TextOps.Read(e, readEntry, text);
                    if (!back.NotApplicable)
                    {
                        if (back.Ex is not null) return $"{readEntry}: reading back '{text}' threw {back.Ex.GetType().Name}: {back.Ex.Message}";
                        if (!back.Ok) return $"{readEntry}: could not read back its own text '{text}'";
                        if (!Same(e, back.Value, vo))
                        {
                            // Differential: the value object only has to round-trip where its wrapped type does
                            // (TimeOnly.ToString() under the invariant culture prints HH:mm and drops the seconds).
                            if (!WrappedLosesItToo(e, plain)) return $"{readEntry}: '{text}' read back as {back.Value}, expected {vo}";
                            r.Note("text:wrapped-round-trip-lossy-too");
                        }
                    }
                }
            }
        }
        return JsonRoundTrip(r, e);
    }

    static string? JsonRoundTrip(Row r, Entry e)
    {
        var o = JsonRow.Apply(r, e).Options!;
        var pos = r["shape.position"]; var con = r.Get("shape.containing") ?? "class";
        var plain = e.Valid(); var vo = Make.Vo(e, plain);
        var hv = Pos.Build(pos, con, e, true, vo); var hp = Pos.Build(pos, con, e, false, plain);
        string jv;
        try { jv = JsonSerializer.Serialize(hv.Value, hv.Declared, o); }
        catch (Exception ex)
        {
            try { JsonSerializer.Serialize(hp.Value, hp.Declared, o); } catch { r.Note("ser-both-threw"); return null; }
            if (r.Is("json.MaxDepth", "3") && ex.Message.Contains("maximum allowed depth")) return null;
            if (r.Is("json.ReferenceHandler", "Preserve") && e.Wrapped == "own-record-class" && ex is NotSupportedException) return null; // documented
            return $"serializing threw {ex.GetType().Name} where the wrapped type did not: {ex.Message}";
        }
        string? Back(string text, out Exception? ex)
        {
            ex = null;
            try { var v = JsonSerializer.Deserialize(text, hv.Declared, o); return v is null ? "null" : JsonSerializer.Serialize(v, hv.Declared, o); } catch (Exception x) { ex = x; return null; }
        }
        var b = Back(jv, out var ex1);
        if (ex1 is not null)
        {
            if (JsonParity.ReadMayDiffer(r, e, "valid", ex1) || r.Get("json.UnmappedMemberHandling") == "Disallow" || (r.Is("json.MaxDepth", "3") && ex1.Message.Contains("maximum allowed depth")) || r.Is("json.ReferenceHandler", "Preserve")) { r.Note("read-back-refused", ex1.GetType().Name); }
            else if (PlainFailsToo(r, hp, o, pos, e)) r.Note("plain-read-back-fails-too");
            else return $"own JSON {Cut(jv)} did not read back: {ex1.GetType().Name}: {Cut(ex1.Message)}";
        }
        else if (b != jv && !r.Is("json.ReferenceHandler", "Preserve")) return $"JSON round trip differs: wrote {Cut(jv)}, read and wrote {Cut(b)}";
        // a changed input must be read or refused with a JsonException (never another type)
        var input = r["input.json"];
        if (input != "valid")
        {
            var text = JsonParity.InputFor(input, e, o, jv, pos);
            Back(text, out var ex2);
            // The application's own converter is in charge under own-for-value-object: what it throws is its own.
            if (ex2 is not null && r.Is("json.Converters", "own-for-value-object")) r.Note("own-vo-converter-threw", ex2.GetType().Name);
            else if (ex2 is not null && ex2 is not (JsonException or NotSupportedException or InvalidOperationException { }))
                return $"input {input}: {ex2.GetType().Name} instead of JsonException: {Cut(ex2.Message)}"
                    + (ex2.InnerException is { } inner ? $" / inner {inner.GetType().FullName}: {Cut(inner.Message)} @ {Cut(inner.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "")}" : "");
        }
        return null;
    }
    static bool PlainFailsToo(Row r, Held hp, JsonSerializerOptions o, string pos, Entry e)
    {
        try { var jp = JsonSerializer.Serialize(hp.Value, hp.Declared, o); JsonSerializer.Deserialize(jp, hp.Declared, o); return false; } catch { return true; }
    }
    static bool PlainHasInner(string text, Held hp, JsonSerializerOptions o)
    { try { JsonSerializer.Deserialize(text, hp.Declared, o); return false; } catch (Exception x) { return x.InnerException is not null; } } // STJ's own inner (the container, a dictionary value) is not the value object's
    internal static string Cut(string? s) => s is null ? "(null)" : s.Length > 160 ? s[..160] + "..." : s;

    // ---- L02: a rejection never repeats the input
    static string? Quotes(Exception ex, string needle, string how, bool stripPath)
    {
        string Msg(Exception x) => stripPath && x.Message.IndexOf(" Path:", StringComparison.Ordinal) is var i and >= 0 ? x.Message[..i] : x.Message;
        if (needle.Length > 0 && Msg(ex).Contains(needle)) return $"{how}: {ex.GetType().Name} quotes the input: {Cut(ex.Message)}";
        if (ex.InnerException is { } inner && needle.Length > 0 && inner.Message.Contains(needle)) return $"{how}: inner {inner.GetType().Name} quotes the input";
        return null;
    }

    public static string? NoQuote(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid();
        var texts = new List<string> { TextOps.Secret };
        if (TextOps.ValidatedKind(e) && e.Refused() is { } rf) texts.Add(TextOps.PlainText(e, rf));
        if (TextOps.HasText(e) && entry is "Parse" or "TryParse" or "IParsable" or "ISpanParsable" or "type-converter")
        {
            foreach (var t in texts)
            {
                var o = TextOps.Read(e, entry, t);
                if (o.NotApplicable) { r.Note("entry-not-applicable", entry); continue; }
                if (o.Ex is not null)
                {
                    if (entry == "TryParse") return $"TryParse threw {o.Ex.GetType().Name} on '{t}': {Cut(o.Ex.Message)}";
                    if (Quotes(o.Ex, t, entry, false) is { } q) return q;
                    if (o.Ex.InnerException is not null && !(entry == "type-converter")) return $"{entry}: refusal of '{t}' carries an inner {o.Ex.InnerException.GetType().Name}";
                    if (entry != "type-converter" && o.Ex is not FormatException) return $"{entry}: refusal of '{t}' is {o.Ex.GetType().Name}, not FormatException: {Cut(o.Ex.Message)}";
                    if (entry == "type-converter" && o.Ex.InnerException is not null) r.Note("converter-wraps-inner");
                }
            }
        }
        else if (entry == "TryFrom" && TextOps.ValidatedKind(e) && e.Refused() is { } rf2)
        {
            var o = TextOps.TryFrom(e, rf2); if (o.Ex is not null) return $"TryFrom(refused) threw {o.Ex.GetType().Name}"; if (o.Ok) return "TryFrom accepted the refused value";
        }
        // JSON half
        var opts = JsonRow.Apply(r, e).Options!;
        var pos = r["shape.position"]; var con = r.Get("shape.containing") ?? "class";
        var hp = Pos.Build(pos, con, e, false, plain); var hv = Pos.Build(pos, con, e, true, Make.Vo(e, plain));
        string jp; string sample;
        try { jp = JsonSerializer.Serialize(hv.Value, hv.Declared, opts); sample = JsonSerializer.Serialize(Make.Unwrap(e, Make.Vo(e, plain)), e.Plain, opts); } catch { r.Note("ser-refused"); return null; }
        if (r.Get("json.Converters") is "own-for-wrapped" or "own-for-value-object") return null; // the application's own converter owns the message
        var at = jp.IndexOf(sample, StringComparison.Ordinal);
        var variants = new List<(string Text, string Needle)>();
        if (at >= 0) variants.Add((jp[..at] + "\"" + TextOps.Secret + "\"" + jp[(at + sample.Length)..], TextOps.Secret));
        var input = r["input.json"];
        if (input != "valid" && at >= 0)
        {
            var text = JsonParity.InputFor(input, e, opts, jp, pos);
            var x = text.Length - jp.Length + sample.Length;
            if (x > 0 && text != jp) { var lit = text.Substring(at, x).Trim('"'); if (lit.Length >= 6 && lit.Length < 60 && lit.All(c => !char.IsPunctuation(c) || c is '-' or ':' or '.')) variants.Add((text, lit)); }
        }
        foreach (var (text, needle) in variants)
        {
            try { JsonSerializer.Deserialize(text, hv.Declared, opts); }
            catch (Exception ex)
            {
                if (ex is JsonException je && Quotes(je, needle, "JSON read", true) is { } q) return q + (pos == "dictionary-key" ? " [dictionary-key]" : "");
                if (ex.InnerException is not null && ex is JsonException && !(ex.InnerException is JsonException) && !PlainHasInner(text, hp, opts)) return $"JSON read: refusal carries inner {ex.InnerException.GetType().Name}: {Cut(ex.InnerException.Message)}";
            }
        }
        return null;
    }

    // ---- L03: one set of rules: every entry runs Create and reports its fault
    public static string? OneRule(Row r, Entry e)
    {
        TextOps.Prepare(r, e);
        var entry = r["text.entry"]; var plain = e.Valid();
        if (!TextOps.ValidatedKind(e) || e.Refused() is not { } refused)
        {
            // no Create: the valid value passes every entry
            if (entry == "TryFrom") { var t = TextOps.TryFrom(e, plain); if (!t.NotApplicable && (!t.Ok || t.Ex is not null)) return "TryFrom(valid) refused on a value object with no rule"; }
            else if (TextOps.HasText(e) && entry is "Parse" or "TryParse" or "IParsable" or "ISpanParsable" or "type-converter")
            { var o = TextOps.Read(e, entry, TextOps.PlainText(e, plain)); if (!o.NotApplicable && (o.Ex is not null || !o.Ok)) return $"{entry}: valid text refused: {o.Ex?.GetType().Name} {Cut(o.Ex?.Message)}"; }
            return JsonOneRule(r, e, false);
        }
        var rtext = TextOps.PlainText(e, refused);
        if (entry == "TryFrom")
        {
            var t = TextOps.TryFrom(e, refused); if (t.Ex is not null) return "TryFrom threw " + t.Ex.GetType().Name; if (t.Ok) return "TryFrom accepted the value Create refuses";
            var kg = Unwrap(() => e.Vo.GetMethod("FromKnownGood")!.Invoke(null, [refused, null]));
            if (kg is not { } kex) return "FromKnownGood accepted the value Create refuses"; if (!kex.Message.Contains("Refused")) return "FromKnownGood does not name the fault: " + Cut(kex.Message);
        }
        else if (TextOps.HasText(e) && entry is "Parse" or "TryParse" or "IParsable" or "ISpanParsable" or "type-converter")
        {
            var o = TextOps.Read(e, entry, rtext);
            if (!o.NotApplicable)
            {
                if (entry == "TryParse") { if (o.Ex is not null) return "TryParse threw " + o.Ex.GetType().Name; if (o.Ok) return "TryParse accepted text Create refuses"; }
                else
                {
                    if (o.Ex is null) return $"{entry} accepted '{rtext}' that Create refuses";
                    if (!o.Ex.Message.Contains("Refused")) return $"{entry}: refusal does not name the fault ({o.Ex.GetType().Name}): {Cut(o.Ex.Message)}";
                    if (entry != "type-converter" && o.Ex is not FormatException) return $"{entry}: refusal is {o.Ex.GetType().Name}, not FormatException";
                }
            }
        }
        return JsonOneRule(r, e, true);
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
    static string? JsonOneRule(Row r, Entry e, bool validated)
    {
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
        var (jp, xp) = Write(hp, o); var (jv, xv) = Write(hv, o);
        // Where the wrapped type alone refuses under the row's options, checked in the same row with the value object refusing alike,
        // the JSON half has nothing to compare and the row is judged by its text entries (as decision 99 judges a document ASP.NET alone refuses).
        const string Alone = "json-half:wrapped-alone-refuses";
        if (xp is not null && xv is not null) { r.Note(Alone, "write"); return null; }
        if (xp is not null) { r.Note("plain-refused", "the wrapped type is not written under these options"); return null; }
        if (xv is not null)
        {
            if (OwnConverterRefusedToo(xv)) { r.Note(Alone, "write, with a converter of the application's for the wrapped type"); return null; }
            r.Note("ser-refused", "the value object is not written where the wrapped type is: JSON parity (L01) judges it"); return null;
        }
        // 1. the valid value reads back as written
        var (back, xb) = Read(jv!, hv, o);
        if (xb is not null)
        {
            if (OwnConverterRefusedToo(xb)) { r.Note(Alone, "read back, with a converter of the application's for the wrapped type"); return null; }
            if (Read(jp!, hp, o).Ex is not null) { r.Note(Alone, "read back"); return null; }
            return $"JSON: the value object does not read back what it writes ({xb.GetType().Name}: {Cut(xb.Message)}): {Cut(jv)}";
        }
        var again = Write(new Held(hv.Declared, back!), o).Json;
        if (again != jv) return $"JSON: read back as {Cut(again)}, written as {Cut(jv)}";
        if (r.Is("json.Converters", "own-for-value-object")) { r.Note("own-vo", "the application's converter takes precedence and reads its own spelling"); return null; }
        if (!validated) return null;
        // 2. the value Create refuses
        var text = JsonParity.InputFor("refused-by-create", e, o, jp!, pos);
        if (text == jp) { r.Note("refused-input-not-placeable", pos); return null; }
        var (accepted, xr) = Read(text, hv, o);
        if (Read(text, hp, o).Ex is not null)
        {
            if (xr is null) return $"JSON read accepted text the wrapped type refuses: {Cut(text)}";
            r.Note(Alone, "the refused value's JSON"); return null;
        }
        if (r.Is("json.Converters", "StoredJsonConverterFactory"))
        {
            if (xr is not null) return $"StoredJsonConverterFactory refused a value Create refuses ({xr.GetType().Name}): it reads stored JSON without validation";
            r.Note("stored-factory-read-unvalidated"); return null;
        }
        if (xr is null) return $"JSON read accepted the value Create refuses: {Cut(text)}";
        if (xr is not JsonException) return $"JSON refusal is {xr.GetType().Name}, not JsonException: {Cut(xr.Message)}";
        if (!xr.Message.Contains("Refused"))
        {
            if (OwnConverterRefusedToo(xr)) { r.Note(Alone, "refused read, with a converter of the application's for the wrapped type"); return null; }
            return $"JsonException does not name the fault: {Cut(xr.Message)}";
        }
        return null;
    }
}
