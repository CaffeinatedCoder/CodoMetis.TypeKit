// Claim C20-testing-shouldhold (src/CodoMetis.TypeKit.Testing/README.md): "ShouldHold(reason) fails unless the fault holds that reason, whichever of its types the reason belongs to. It hands back the Fault<TFault> with its detail and rule intact."
// "An uninitialized result fails every assertion." "A failure says what the outcome was" and "never prints a value".
// Oracle: claim. Held reason -> returns the Fault<TW> with Reason/Detail/Rule intact; wrong reason (same type other value, or a fault of another type) -> TypeKitAssertionException naming the fault; default composed fault -> TypeKitAssertionException "uninitialized".
#pragma warning disable xUnit1051, CMTK0014, CMTK0010 // CMTK0010: TW is a run-time-chosen type, distinct from the fixed Alt enums by construction
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.Testing;
using Probes.Shapes;
namespace Probes;
public enum C20Alt1 { A, B }
public enum C20Alt2 { A, B }
public enum C20RuleKind { Strict }
public enum C20WEnum { One, Two }
[Flags] public enum C20WFlags { None = 0, X = 1, Y = 2 }
public sealed record C20Own(int N);
public sealed class C20ShouldHoldProbe
{
    public static TheoryData<string, int> Data() => Rows.Data("C20-testing-shouldhold");
    [Theory, MemberData(nameof(Data))]
    public Task Row(string claim, int idx) => Rec.Run(claim, idx, Check);

    static Task<string?> Check(Row r) => r["shape.wrapped"] switch
    {
        "string" => Go(r, "a", "b"), "Guid" => Go(r, Guid.Empty, Guid.NewGuid()), "int" => Go(r, 1, 2), "long" => Go(r, 1L, 2L), "byte" => Go<byte>(r, 1, 2),
        "ulong" => Go<ulong>(r, 1, 2), "decimal" => Go(r, 1.5m, 2.5m), "double" => Go(r, 1.5, 2.5), "bool" => Go(r, true, false), "char" => Go(r, 'a', 'b'),
        "DateTime" => Go(r, new DateTime(2026, 1, 1), new DateTime(2026, 1, 2)), "DateTimeOffset" => Go(r, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
        "DateOnly" => Go(r, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)), "TimeOnly" => Go(r, new TimeOnly(1, 0), new TimeOnly(2, 0)), "TimeSpan" => Go(r, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        "enum" => Go(r, C20WEnum.One, C20WEnum.Two), "flags-enum" => Go(r, C20WFlags.X, C20WFlags.Y), "Uri" => Go(r, new Uri("http://a/"), new Uri("http://b/")),
        "NodaTime.Instant" => Go(r, NodaTime.Instant.FromUnixTimeSeconds(1), NodaTime.Instant.FromUnixTimeSeconds(2)), "own-record-class" => Go(r, new C20Own(1), new C20Own(2)),
        var w => throw new ArgumentException(w),
    };

    static async Task<string?> Go<TW>(Row r, TW a, TW b) where TW : notnull
    {
        var arity = r["arity"]; var first = r["set"] == "first"; var wrong = r["wrong-reason"] == "yes";
        if (arity == "2")
            return first ? await Run<TW, Fault<TW, C20Alt1>>(r, a, b, x => Fault<TW, C20Alt1>.Of(x), f => f.ShouldHold(a), f => f.ShouldHold(b), Fault<TW, C20Alt1>.Of(C20Alt1.A), f => f.ShouldHold(C20Alt1.A))
                          : await Run<TW, Fault<C20Alt1, TW>>(r, a, b, x => Fault<C20Alt1, TW>.Of(x), f => f.ShouldHold(a), f => f.ShouldHold(b), Fault<C20Alt1, TW>.Of(C20Alt1.A), f => f.ShouldHold(C20Alt1.A));
        return first ? await Run<TW, Fault<TW, C20Alt1, C20Alt2>>(r, a, b, x => Fault<TW, C20Alt1, C20Alt2>.Of(x), f => f.ShouldHold(a), f => f.ShouldHold(b), Fault<TW, C20Alt1, C20Alt2>.Of(C20Alt2.A), f => f.ShouldHold(C20Alt2.A))
                      : await Run<TW, Fault<C20Alt1, C20Alt2, TW>>(r, a, b, x => Fault<C20Alt1, C20Alt2, TW>.Of(x), f => f.ShouldHold(a), f => f.ShouldHold(b), Fault<C20Alt1, C20Alt2, TW>.Of(C20Alt1.A), f => f.ShouldHold(C20Alt1.A));
    }

    static async Task<string?> Run<TW, TC>(Row r, TW a, TW b, Func<TW, TC> make, Func<TC, Fault<TW>> holdA, Func<TC, Fault<TW>> holdB, TC foreign, Func<TC, object> holdForeignSet)
        where TW : notnull where TC : struct
    {
        var wrong = r["wrong-reason"] == "yes";
        // the held fault: with wrong-reason=yes the carrier holds a fault of ANOTHER type (foreign) and the test asks for TW's reason a
        TC held = wrong ? foreign : make(a);
        string? fail = null;
        var op = r["or.operation"]; var shape = r["or.shape"];
        TC? got = await Carry(r, held, op, shape);
        if (got is null) return null; // inapplicable, noted
        var composed = got.Value;
        Fault<TW>? hand = null; Exception? thrown = null;
        try { hand = holdA(composed); } catch (Exception ex) { thrown = ex; }
        if (op == "default-instance")
        {
            if (thrown is not TypeKitAssertionException u || !u.Message.Contains("uninitialized")) return $"default composed fault: ShouldHold gave {thrown?.GetType().Name ?? "no exception"} {thrown?.Message}; expected TypeKitAssertionException 'uninitialized'";
            return null;
        }
        if (wrong)
        {
            if (thrown is not TypeKitAssertionException e) return $"ShouldHold({a}) on a fault of another type gave {(thrown is null ? "a result: " + hand : thrown.GetType().Name)}; expected TypeKitAssertionException";
            if (!e.Message.Contains("Expected the fault") || e.Message.Contains("UNEXPECTED")) return "message: " + e.Message;
            // same type other value
            try { holdB(composed); return "ShouldHold(other value) passed on a fault of another type"; } catch (TypeKitAssertionException) { }
            // and the held reason itself is still reachable by its own type
            try { holdForeignSet(composed); } catch (Exception ex) { return "ShouldHold(own reason) threw " + ex.GetType().Name + ": " + ex.Message; }
            return null;
        }
        if (thrown is not null) return $"ShouldHold({a}) on a fault holding it threw {thrown.GetType().Name}: {thrown.Message}";
        if (!EqualityComparer<TW>.Default.Equals(hand!.Value.Reason, a)) return $"returned fault has reason {hand.Value.Reason}, expected {a}";
        try { holdB(composed); fail = "ShouldHold(other value of the same type) passed"; } catch (TypeKitAssertionException) { }
        return fail;
    }

    // Carries the fault through the carrier and the operation, then extracts it with the Testing assertions. null = the combination has no such operation (noted inapplicable).
    static async Task<TC?> Carry<TC>(Row r, TC fault, string op, string shape) where TC : struct
    {
        if (op == "default-instance") return default(TC);
        // Operations the carrier has no member for run as Match, so the carrier's other pairs earn credit: Option has no Result.Catch (Catch returns a Result); Result<E> has no Select/SelectMany (query syntax) and no FirstOrNone (Option-only).
        if ((shape == "Option" && op == "Result.Catch") || (shape == "Result-E" && op is "query-syntax" or "FirstOrNone"))
        {
            // One of the two pairs of the row cannot be earned; alternate which axis runs at a value that exists, so the carrier's and the operation's pairs both get credit across rows.
            if (r.Index % 2 == 0) { r.Substituted("or.operation", "Match", $"{op} has no member on {shape}"); op = "Match"; }
            else { r.Substituted("or.shape", "Result-T-E", $"{op} has no member on {shape}"); shape = "Result-T-E"; }
        }
        Result<int, TC> res = Result.Error(fault);
        switch (shape)
        {
            case "Result-T-E" or "Task-of-Result" or "ValueTask-of-Result":
                switch (op)
                {
                    case "Match": res = res.Match<Result<int, TC>>(v => v, e => Result.Error(e)); break;
                    case "Bind-Map-chain": res = Result<int, TC>.Success(1).Bind(_ => res).Map(x => x + 1); break;
                    case "query-syntax": res = from x in res select x + 1; break;
                    case "Sequence-Transpose": { var seq = new[] { res }.Sequence(); var e = seq.ShouldBeError(); res = Result.Error(e); break; }
                    case "Result.Catch": res = Result.Catch<int>((Func<int>)(() => throw new InvalidOperationException())).MapError(_ => fault); break;
                    case "FirstOrNone": { var o = new[] { fault }.FirstOrNone(); res = Result.Error(o.ShouldBeSome()); break; }
                    case "after-await-elsewhere": await Task.Yield(); res = await Task.Run(() => res).ConfigureAwait(false); break;
                    default: throw new ArgumentException(op);
                }
                if (shape == "Task-of-Result") return await Task.FromResult(res).ConfigureAwait(false) is var t ? t.ShouldBeError() : null;
                if (shape == "ValueTask-of-Result") return (await new ValueTask<Result<int, TC>>(res)).ShouldBeError();
                return res.ShouldBeError();
            case "Result-E":
            {
                Result<TC> re = Result.Error(fault);
                switch (op)
                {
                    case "Match": re = re.Match<Result<TC>>(() => Result<TC>.Success(), e => Result.Error(e)); break;
                    case "Bind-Map-chain": re = Result<TC>.Success().Bind(() => re); break;
                    case "Sequence-Transpose": re = new[] { re }.Sequence(); break;
                    case "Result.Catch": re = Result.Catch((Action)(() => throw new InvalidOperationException())).MapError(_ => fault); break;
                    case "after-await-elsewhere": await Task.Yield(); re = await Task.Run(() => re).ConfigureAwait(false); break;
                    default: r.Note(NoteKeys.EntryNotApplicable, op + " on Result-E"); return null;
                }
                return re.ShouldBeError();
            }
            default: // Option: the fault as the content of a Some
            {
                var opt = Option.Some(fault);
                switch (op)
                {
                    case "Match": opt = opt.Match(v => Option.Some(v), () => Option.None<TC>()); break;
                    case "Bind-Map-chain": opt = Option.Some(1).SelectMany(_ => opt); break;
                    case "query-syntax": opt = from x in opt select x; break;
                    case "FirstOrNone": opt = new[] { fault }.FirstOrNone(); break;
                    case "Sequence-Transpose": { var back = Option.Some<Result<int, TC>>(Result.Error(fault)).Transpose().ShouldBeError(); opt = Option.Some(back); break; }
                    case "after-await-elsewhere": await Task.Yield(); opt = await Task.Run(() => opt).ConfigureAwait(false); break;
                    default: r.Note(NoteKeys.EntryNotApplicable, op + " on Option"); return null;
                }
                return opt.ShouldBeSome();
            }
        }
    }
}
