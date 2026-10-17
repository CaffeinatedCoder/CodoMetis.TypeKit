// Claim L06-off-the-wire (README.md#what-typekit-guarantees): "Option and Result stay off the wire. System.Text.Json throws
// NotSupportedException for either, in both directions, instead of writing {} that reads back as None. [...] If you want a wire format of
// your own, a converter you register on the options takes precedence." The message names the type (src/CodoMetis.TypeKit/README.md
// "Option or Result in JSON"); a source-generated context refuses an Option<T>? or Result<...>? property with the serializer's own
// InvalidOperationException when it builds that contract (ibid., "Good to know").
// Oracle: exception, in every position, with the row's options and input, for an Option or Result produced by the row's operation. A
// converter of the application's for the same type in the same row is the baseline: it must win in both directions, and where the options
// leave the member out (it writes and reads nothing there), no converter runs, which is intended ("A missing property reaches no converter").
#pragma warning disable CMTK0001 // default-instance: the default Option and Result are the shapes under test
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using Probes.Shapes;
namespace Probes.Json;

/// <summary>An application's own converter for an Option or Result: writes "own-or" and reads it back as the value it was made with, counting reads.</summary>
public sealed class OwnOrConverter<T>(T value) : JsonConverter<T>
{
    public int Reads;
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Reads++;
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName) || reader.GetString() != "own-or") throw new JsonException("not own-or");
        return value;
    }
    public override void Write(Utf8JsonWriter writer, T v, JsonSerializerOptions options) => writer.WriteStringValue("own-or");
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Read(ref reader, typeToConvert, options);
    public override void WriteAsPropertyName(Utf8JsonWriter writer, T v, JsonSerializerOptions options) => writer.WritePropertyName("own-or");
}

public static class OffTheWire
{
    public static async Task<string?> Check(Row r)
    {
        var kind = r["shape.kind"];
        var e = kind == "closed-set" ? Registry.Get(kind, "-") : Registry.Get(kind, r["shape.wrapped"]);
        var position = r["shape.position"];
        var containing = position == "root" ? "class" : r["shape.containing"];
        var shape = r["or.shape"]; var op = r["or.operation"];
        _ = r["input.json"];   // the row's input, which every check of the row runs under; the read applies it
        if (shape == "Result-E" && op is "query-syntax" or "FirstOrNone")
        { r.Substituted("or.operation", "Match", "Result<TError> has no " + op + " (claims.json excludes the pair)"); op = "Match"; }
        var voValue = Make.Vo(e, e.Valid());
        var v = Make.Held(e.Vo);
        var produce = typeof(OffTheWire).GetMethod(nameof(Produce), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(v);
        var (orType, orValue) = await (Task<(Type, object)>)produce.Invoke(null, [shape, op, voValue])!;
        var key = shape == "Option" ? "O" : shape == "Result-E" ? "RE" : "RTE";
        var pb = typeof(OffTheWire).Assembly.GetType($"Probes.Json.L06PB_{v.Name}_{key}")!; var pd = typeof(OffTheWire).Assembly.GetType($"Probes.Json.L06PD_{v.Name}_{key}")!;
        var eo = e with { Vo = orType, Plain = orType, PolyBase = pb, PolyDerived = pd, PolyBasePlain = pb, PolyDerivedPlain = pd };
        var hv = Pos.Build(position, containing, e, true, voValue);     // the value object's shape: its document is the read's input
        var ho = Pos.Build(position, containing, eo, true, orValue);
        var o = JCtx.Apply(r, e, hv.Declared, ho.Declared);
        var own = (JsonConverter)Activator.CreateInstance(typeof(OwnOrConverter<>).MakeGenericType(orType), orValue)!;
        var oOwn = new JsonSerializerOptions(o); oOwn.Converters.Insert(0, own);
        int OwnReads() => (int)own.GetType().GetField("Reads")!.GetValue(own)!;
        var name = Jx.Name(orType);
        // not-promised a-source-generated-context-refuses-a-nullable
        var nullableUnderContext = r.Is("json.TypeInfoResolver", "source-generated-context") && position == "nullable-property";
        string? Refused(Exception? x, string dir)
        {
            if (nullableUnderContext)
                return x is InvalidOperationException ? null : $"{dir}: a source-generated context refuses an {name}? property with the serializer's InvalidOperationException; got {(x is null ? "no exception" : Jx.Ex(x))}";
            if (x is not NotSupportedException) return $"{dir}: expected NotSupportedException naming {name}, got {(x is null ? "no exception" : Jx.Ex(x))}";
            // TypeKit's refusal, told by its type and never by its words: the serializer passes a converter's NotSupportedException on as the
            // inner exception of its own, so one in the chain is thrown by NotWireTypeJsonConverterFactory's converter, by nameof
            if (!ThrownBy(x, typeof(NotWireTypeJsonConverterFactory))) return $"{dir}: the NotSupportedException is not {nameof(NotWireTypeJsonConverterFactory)}'s: {Jx.Ex(x)}";
            return null;
        }

        // the baseline: the application's own converter for the type wins, in both directions
        var (jOwn, xOwn) = Jx.Ser(ho, oOwn);
        var (jv, xv) = Jx.Ser(hv, o);
        var refused = Baseline.Refusal;
        var fw = r.Check("never-written", refused, c =>
        {
            if (xOwn is not null)
            {
                if (xv is not null && xv.GetType() == xOwn.GetType()) return c.Inconclusive("write: the options refuse the shape itself, with the application's converter alike: " + Jx.Ex(xv));
                return $"write: a converter registered on the options for {name} does not take precedence: {Jx.Ex(xOwn)}";
            }
            var ownWritten = Jx.Tokens(jOwn!).Any(t => t.Text == "own-or");
            var (jo, xo) = Jx.Ser(ho, o);
            if (xo is null)
            {
                if (ownWritten || jo != jOwn) return $"write: {name} was written: {Jx.Cut(jo)}";
                // the options leave the member out, the application's converter's document alike: no converter ran
                c.Note(NoteKeys.JsonMemberLeftOutByOptions, "write: " + Jx.Cut(jo));
                return c.Inconclusive("write: the options leave the member out, so no converter runs: " + Jx.Cut(jo));
            }
            if (Refused(xo, "write") is { } f) return f;
            c.Note(NoteKeys.Compared, $"{name} by {op}, {position}: write refused");
            return null;
        });
        // reading: the value object's document with the row's input, where the Option or Result stands; measured before the check, so that
        // a document the serializer refuses before the member (side (1), Sides) decides the check's baseline
        var full = Jx.Full(o);
        var (jf, xf) = Jx.Ser(hv, full);
        // does the application's converter get called there? Its own document, read under the same options, says so
        var (jOwnFull, _) = Jx.Ser(ho, Jx.Full(oOwn));
        string input = r["input.json"], text = "";
        if (xf is null) text = JsonParity.InputFor(input, e, full, jf!, position);
        else if (jOwnFull is not null) { input = "valid"; text = jOwnFull; }
        var reads = xOwn is null && (xf is null || jOwnFull is not null);
        var (_, xOwnRead) = reads ? Jx.De(jOwnFull ?? "null", ho.Declared, oOwn) : (null, null);
        var memberRead = reads && OwnReads() > 0;
        var (vr, xr) = reads ? Jx.De(text, ho.Declared, o) : (null, null);
        // side (1): the same document with the value object where the Option or Result stands, read under the same options; refusing alike
        // (the same exception type at the same path, no message compared), the serializer refused the document before the member
        var three = new Sides.Answer("read", null, xr);
        var one = new Sides.Answer("read", null, reads && xr is not null && !ThrownBy(xr, typeof(NotWireTypeJsonConverterFactory)) ? Jx.De(text, hv.Declared, o).Ex : null);
        var framework = Sides.Alike(one, three) ? Sides.Measured(one, "the document with the value object where the Option or Result stands") : null;
        var fr = await Sides.Check(r, "never-read", refused, framework, three, null, null, async c =>
        {
            await Task.CompletedTask;
            if (xOwn is not null) return c.Inconclusive("read: the options refuse the shape itself, so no document has the member: " + Jx.Ex(xOwn));
            _ = c["input.json"];
            if (xf is not null)
            {
                // the value object's own shape cannot be written here (a value object over a class under Preserve, a class as a key): the
                // document with the application's converter has a token where the Option or Result stands, and is read as it is
                if (jOwnFull is null) return c.Inconclusive("read: no document has the member: " + Jx.Ex(xf));
                c.Substituted("input.json", "valid", "the value object's document cannot be written: " + xf.GetType().Name);
            }
            if (xOwnRead is not null) return $"read: a converter registered on the options for {name} does not take precedence: {Jx.Ex(xOwnRead)}";
            if (xr is null && input == "null" && position == "in-patch" && Jx.Member(ho.Declared, vr) is { } patch && patch.GetType().GetProperty("State")!.GetValue(patch)!.ToString() == "Null")
                // a JSON null is the patch's own null (docs/merge-patches.md): it holds no Option or Result, and none was read
                return c.Inapplicable(Cite.Docs("docs/merge-patches.md#tell-absent-null-and-a-value-apart"), $"read null: the Patch<{name}> is null, nothing of {name} read");
            if (xr is null)
            {
                if (memberRead) return $"read {input}: {name} was read without an exception\n  text={Jx.Cut(text)}";
                c.Note(NoteKeys.JsonMemberLeftOutByOptions, "read " + input);
                return c.Inapplicable(Cite.NotPromised("a-missing-property-reaches-no-converter"), "read " + input);
            }
            if (Refused(xr, "read " + input) is { } f) return f + $"\n  text={Jx.Cut(text)}";
            c.Note(NoteKeys.Compared, $"{name} by {op}, {position}: read {input} refused");
            return null;
        });
        return fw ?? fr;
    }

    /// <summary>
    /// Whether a refusal, or one in its chain of inner exceptions, was thrown by a converter that <paramref name="factory"/> declares: the
    /// method that threw it (its TargetSite) belongs to the factory's type or to a type nested in it. Told by types, never by a message.
    /// </summary>
    static bool ThrownBy(Exception? x, Type factory)
    {
        for (; x is not null; x = x.InnerException)
            for (var t = x.TargetSite?.DeclaringType; t is not null; t = t.DeclaringType)
                if (t == factory) return true;
        return false;
    }

    static async Task<(Type, object)> Produce<V>(string shape, string op, V v) where V : notnull
    {
        switch (shape)
        {
            case "Option":
            {
                Option<V> x = op switch
                {
                    "Match" => Option.Some(v).Match(s => Option.Some(s), () => Option.None<V>()),
                    "Bind-Map-chain" => Option.Some(v).Bind(s => Option.Some(s)).Map(s => s),
                    "query-syntax" => from s in Option.Some(v) select s,
                    "Sequence-Transpose" => Option.Some(Result<V, ShapeFault>.Success(v)).Transpose().Match(s => s, _ => Option.None<V>()),
                    "Result.Catch" => Result.Catch(() => v).ToOption(),
                    "FirstOrNone" => new[] { v }.FirstOrNone(),
                    "default-instance" => default,
                    "after-await-elsewhere" => await Task.Run(() => Option.Some(v)).ConfigureAwait(false),
                    _ => throw new ArgumentException(op),
                };
                return (typeof(Option<V>), x);
            }
            case "Result-E":
            {
                Result<V> x = op switch
                {
                    "Match" => Result<V>.Error(v).Match(() => Result<V>.Success(), er => Result<V>.Error(er)),
                    "Bind-Map-chain" => Result<V>.Success().Bind(() => Result<V>.Error(v)),
                    "Sequence-Transpose" => new[] { Result<V>.Error(v) }.Sequence(),
                    "Result.Catch" => Result.Catch(() => { }).MapError(_ => v),
                    "default-instance" => default,
                    "after-await-elsewhere" => await Task.Run(() => Result<V>.Error(v)).ConfigureAwait(false),
                    _ => throw new ArgumentException(op),
                };
                return (typeof(Result<V>), x);
            }
            default:
            {
                Result<V, ShapeFault> x;
                if (shape == "Task-of-Result")
                {
                    var ok = Task.FromResult(Result<V, ShapeFault>.Success(v));
                    x = op switch
                    {
                        "Bind-Map-chain" => await ok.BindAsync(s => Result<V, ShapeFault>.Success(s)).MapAsync(s => s),
                        "FirstOrNone" => await Task.FromResult(new[] { v }.FirstOrNone()).ToResultAsync(ShapeFault.Refused),
                        "Result.Catch" => await Result.CatchAsync(() => Task.FromResult(v)).MapErrorAsync(_ => ShapeFault.Refused),
                        _ => await Task.FromResult(await Sync(op, v)).ConfigureAwait(false),
                    };
                }
                else if (shape == "ValueTask-of-Result") x = await new ValueTask<Result<V, ShapeFault>>(await Sync(op, v));
                else x = await Sync(op, v);
                return (typeof(Result<V, ShapeFault>), x);
            }
        }
    }

    static async Task<Result<V, ShapeFault>> Sync<V>(string op, V v) where V : notnull => op switch
    {
        "Match" => Result<V, ShapeFault>.Success(v).Match(s => Result<V, ShapeFault>.Success(s), er => Result<V, ShapeFault>.Error(er)),
        "Bind-Map-chain" => Result<V, ShapeFault>.Success(v).Bind(s => Result<V, ShapeFault>.Success(s)).Map(s => s),
        "query-syntax" => from s in Result<V, ShapeFault>.Success(v) select s,
        "Sequence-Transpose" => new[] { Result<V, ShapeFault>.Success(v) }.Sequence().Map(l => l[0]),
        "Result.Catch" => Result.Catch(() => v).MapError(_ => ShapeFault.Refused),
        "FirstOrNone" => new[] { v }.FirstOrNone().ToResult(ShapeFault.Refused),
        "default-instance" => default,
        "after-await-elsewhere" => await Task.Run(() => Result<V, ShapeFault>.Success(v)).ConfigureAwait(false),
        _ => throw new ArgumentException(op),
    };
}
