// Claim C11-parse-shapes-build (CHANGELOG.md 2.1.0, Fixed): "A wrapped type's `Parse` whose result can't be assigned to it, or that
// takes its text by `ref`, or an abstract class's string constructor, no longer breaks the build; the value object has no parsing."
// With Generators README "What you can wrap" ("Parsing where it can be parsed") and "What is generated" (TryFrom for IValidatedValue,
// the [TypeConverter] when parsing is generated, ToString() the underlying type's own text under the invariant culture).
// Oracle: claim; for the ordinary shape, differential against the catalogue's top-level value object of the same kind and wrapped type
// (Probes.Shapes.Registry). Each row's value object (harness/C11Shapes.g.cs, generated from this claim's rows) is of shape.kind at
// shape.site with shape.inheritance, over the parse shape's own class (implicit-result: Parse returns an int; ref-text: Parse or the
// string constructor takes the text by ref; abstract-string-ctor) or, for ordinary, over shape.wrapped. That it is in this assembly is
// the build half. Under text.culture and process.converter-lookup, the row's text.entry is then checked: over a parse shape, no
// Parse, TryParse, IParsable, ISpanParsable or string type converter, while TryFrom (validated) and formatting work; over the ordinary
// shape, the entry exists exactly where the catalogue's does, parses where the wrapped type parses, and gives the catalogue's result.
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Probes.Build;
using Probes.Build.C11;
using Probes.Shapes;
namespace Probes;

public sealed class C11ParseShapesBuildProbe
{
    const string Claim = "C11-parse-shapes-build";

    public static TheoryData<string, int> Data() => Rows.Data(Claim);

    [Theory, MemberData(nameof(Data))]
    public Task Probe(string claim, int idx) => Rec.Run(claim, idx, r => Task.FromResult(Check(r)));

    static readonly string[] ParseEntries = ["Parse", "TryParse", "IParsable", "ISpanParsable", "type-converter"];

    static string? Check(Probes.Shapes.Row r)
    {
        var c = C11Table.Cases.SingleOrDefault(x => x.Index == r.Index);
        if (c is null || c.Key != Decls.Key(Claim, r.Index))
        { r.Outcome("harness-error", "C11Shapes.g.cs was generated from other rows: regenerate it with the rows"); return null; }
        foreach (var (axis, value) in c.Substituted) r.Substituted(axis, value, "the declaration cannot exist as the row asks (excluded in claims.json)");
        _ = r["shape.kind"]; _ = r["shape.site"]; _ = r["shape.inheritance"];
        var shape = r["parse-shape"]; var entry = r["text.entry"];
        var ordinary = shape == "ordinary";
        var wrapped = ordinary ? r["shape.wrapped"] : null;
        object sample = shape switch
        {
            "ordinary" => Registry.Get("record-struct", wrapped!).Valid(),
            "implicit-result" => new C11ParsesToInt(),
            "ref-text" => c.Wrapped == typeof(C11ParsesByRef) ? new C11ParsesByRef() : C11ConstructedByRef.Sample(),
            "abstract-string-ctor" => new C11ConcreteText("abstract-text"),
            _ => throw new ArgumentException(shape),
        };
        if (!c.Wrapped.IsInstanceOfType(sample)) { r.Outcome("harness-error", $"the sample is no {c.Wrapped.Name}"); return null; }
        var e = new Entry(c.Kind, wrapped ?? shape, c.Vo, c.Wrapped, () => sample, () => null, typeof(object), typeof(object), typeof(object), typeof(object));
        TextOps.Prepare(r, e);
        var vo = Make.Vo(e, sample);
        if (!Equals(Make.Unwrap(e, vo), sample)) return $"the value object does not hold the value it was made from";
        if (ordinary) return Ordinary(r, e, entry, wrapped!, sample, vo);
        return NoParsing(r, e, entry, sample, vo) ?? Control(c);
    }

    /// <summary>
    /// The parse shape's control, the same declaration over the nearest type that parses: it has IParsable, and Parse gives back the text
    /// it was given. Without it a generator that took parsing from every class would pass the shapes.
    /// </summary>
    static string? Control(C11Case c)
    {
        if (c.Control is null) throw new InvalidOperationException($"row {c.Index} has no control");
        if (!TextOps.Implements(typeof(IParsable<>), c.Control)) return $"the control over {c.ControlWrapped!.Name}, which can be parsed, has no IParsable";
        var parse = c.Control.GetMethod("Parse", [typeof(string), typeof(IFormatProvider)]);
        if (parse is null) return $"the control over {c.ControlWrapped!.Name} has no Parse(string, IFormatProvider)";
        object? parsed;
        try { parsed = parse.Invoke(null, ["control-text", null]); }
        catch (TargetInvocationException t) { return $"the control's Parse threw {t.InnerException!.GetType().Name}: {t.InnerException.Message}"; }
        var value = c.Control.GetProperty("Value")!.GetValue(parsed);
        return value?.ToString() == "control-text" ? null : $"the control's Parse gave back '{value}', not 'control-text'";
    }

    static bool Has(Type t, string name) => t.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == name);

    static string? NoParsing(Probes.Shapes.Row r, Entry e, string entry, object sample, object vo)
    {
        var t = e.Vo;
        // The baseline every row checks: no member of parsing at all.
        var parsing = new List<string>();
        if (Has(t, "Parse")) parsing.Add("Parse");
        if (Has(t, "TryParse")) parsing.Add("TryParse");
        if (TextOps.Implements(typeof(IParsable<>), t)) parsing.Add("IParsable");
        if (TextOps.Implements(typeof(ISpanParsable<>), t)) parsing.Add("ISpanParsable");
        if (TextOps.Implements(typeof(IUtf8SpanParsable<>), t)) parsing.Add("IUtf8SpanParsable");
        if (t.GetCustomAttribute<TypeConverterAttribute>() is not null || TypeDescriptor.GetConverter(t).CanConvertFrom(typeof(string))) parsing.Add("a type converter from string");
        if (parsing.Count > 0) return $"the value object over a {r["parse-shape"]} type has parsing: {string.Join(", ", parsing)}";
        var text = sample.ToString()!;
        switch (entry)
        {
            case "Parse" or "TryParse" or "IParsable" or "ISpanParsable" or "type-converter":
            {
                var o = TextOps.Read(e, entry, text);
                if (!o.NotApplicable) return $"{entry} reads text on a value object documented to have no parsing";
                break;
            }
            case "TryFrom":
            {
                var o = TextOps.TryFrom(e, sample);
                if (TextOps.ValidatedKind(e))
                {
                    if (o.NotApplicable) return "a validated value object has no TryFrom";
                    if (o.Ex is not null || !o.Ok || !Equals(Make.Unwrap(e, o.Value!), sample)) return $"TryFrom(valid) gave ok={o.Ok} {o.Ex?.GetType().Name}";
                }
                else if (Has(e.Vo, "TryFrom")) return "a plain value object has a TryFrom";
                break;
            }
            case "ToString-format":
            {
                if (vo.ToString() != text) return $"ToString() is '{vo}', the wrapped value's own text is '{text}'";
                if (vo is IFormattable f && f.ToString(null, CultureInfo.InvariantCulture) != text) return $"IFormattable.ToString is '{f.ToString(null, CultureInfo.InvariantCulture)}', expected '{text}'";
                break;
            }
            case "TryFormat-short-span":
            {
                if (vo.ToString() != text) return $"ToString() is '{vo}', the wrapped value's own text is '{text}'";
                if (vo is ISpanFormattable)
                {
                    var full = new object?[] { vo, 4096, null, null };
                    var written = (string)typeof(TextOps).GetMethod(nameof(TextOps.FS))!.MakeGenericMethod(e.Vo).Invoke(null, full)!;
                    if (!(bool)full[2]! || written != text) return $"TryFormat wrote '{written}' (ok={full[2]}), the wrapped value's text is '{text}'";
                    var shortArgs = new object?[] { vo, Math.Max(0, text.Length - 1), null, null };
                    typeof(TextOps).GetMethod(nameof(TextOps.FS))!.MakeGenericMethod(e.Vo).Invoke(null, shortArgs);
                    if ((bool)shortArgs[2]!) return $"TryFormat into a span of {text.Length - 1} said '{text}' fit";
                }
                break;
            }
            default: throw new ArgumentException("text.entry " + entry);
        }
        r.Note("compared", $"no parsing; {entry}; the control parses");
        return null;
    }

    static string? Ordinary(Probes.Shapes.Row r, Entry e, string entry, string wrapped, object sample, object vo)
    {
        var reference = Registry.Get(e.Kind, wrapped);
        var refVo = Make.Vo(reference, sample);
        string? Same(Out mine, Out theirs, Entry me, Entry them, string what)
        {
            if (mine.NotApplicable != theirs.NotApplicable) return $"{what}: {(mine.NotApplicable ? "absent" : "present")} here, {(theirs.NotApplicable ? "absent" : "present")} on the catalogue's top-level value object";
            if (mine.NotApplicable) return null;
            if ((mine.Ex is null) != (theirs.Ex is null) || mine.Ex?.GetType() != theirs.Ex?.GetType())
                return $"{what}: {(mine.Ex is null ? "no exception" : mine.Ex.GetType().Name + ": " + mine.Ex.Message)} here, {(theirs.Ex is null ? "no exception" : theirs.Ex.GetType().Name)} on the catalogue's";
            if (mine.Ok != theirs.Ok) return $"{what}: ok={mine.Ok} here, {theirs.Ok} on the catalogue's";
            if (mine.Text != theirs.Text) return $"{what}: '{mine.Text}' here, '{theirs.Text}' on the catalogue's";
            if (mine.Value is not null && theirs.Value is not null && !Equals(Make.Unwrap(me, mine.Value), Make.Unwrap(them, theirs.Value)))
                return $"{what}: {Make.Unwrap(me, mine.Value)} here, {Make.Unwrap(them, theirs.Value)} on the catalogue's";
            return null;
        }
        string? fail;
        if (ParseEntries.Contains(entry))
        {
            var text = TextOps.PlainText(reference, sample);
            var mine = TextOps.Read(e, entry, text);
            if (mine.NotApplicable && wrapped != "own-record-class" && entry is not "ISpanParsable")
                return $"{entry}: the value object over {wrapped}, which parses, has no {entry}";
            fail = Same(mine, TextOps.Read(reference, entry, text), e, reference, entry);
        }
        else if (entry == "TryFrom")
        {
            var mine = TextOps.TryFrom(e, sample);
            if (TextOps.ValidatedKind(e) && (mine.NotApplicable || !mine.Ok)) return "TryFrom(valid) on a validated value object gave no value";
            fail = Same(mine, TextOps.TryFrom(reference, sample), e, reference, "TryFrom");
        }
        else
        {
            fail = Same(TextOps.Write(e, entry, vo), TextOps.Write(reference, entry, refVo), e, reference, entry);
            if (fail is null && entry == "TryFormat-short-span" && vo is ISpanFormattable)
            {
                var text = TextOps.Write(e, entry, vo).Text ?? "";
                var shortArgs = new object?[] { vo, Math.Max(0, text.Length - 1), null, null };
                typeof(TextOps).GetMethod(nameof(TextOps.FS))!.MakeGenericMethod(e.Vo).Invoke(null, shortArgs);
                if (text.Length > 0 && (bool)shortArgs[2]!) fail = $"TryFormat into a span of {text.Length - 1} said '{text}' fit";
            }
        }
        if (fail is null) r.Note("compared", $"{entry} against the catalogue's {e.Kind} over {wrapped}");
        return fail;
    }
}
