using CodoMetis.TypeKit;
using CodoMetis.TypeKit.Testing;

Option<int> some = Option.Some(3);
Option<int> none = Option.None();
Console.WriteLine($"some={some.Match(v => $"Some {v}", () => "None")}");
Console.WriteLine($"none={none.Match(v => $"Some {v}", () => "None")}");
Console.WriteLine($"printed=[{Option.Some(424242)}]");

Result<int, string> ok = 5;
Result<int, string> refused = Result.Error("nope");
Console.WriteLine($"ok={ok.Match(v => $"Ok {v}", e => $"Error {e}")}");
Console.WriteLine($"refused={refused.Match(v => $"Ok {v}", e => $"Error {e}")}");

// The command shape the README shows: the success marker and a bare error, one conditional.
Result<string> Cancel(bool removed) => removed ? Result.Success() : "gone";
Console.WriteLine($"cancelled={Cancel(true).Match(() => "Ok", e => $"Error {e}")}/{Cancel(false).Match(() => "Ok", e => $"Error {e}")}");

// An array slot is the one place a default Result still comes from: it must not pick a branch.
var slots = new Result<int, string>[1];
var slot = slots[0];
try
{
    slot.Match(_ => "", _ => "");
    Console.WriteLine("uninitialized=picked a branch");
}
catch (InvalidOperationException)
{
    Console.WriteLine("uninitialized=throws");
}

// The test assertions, from the package as a test project installs it.
Console.WriteLine($"asserted={some.ShouldBeSome()}/{refused.ShouldBeError()}");
try { ok.ShouldBeError("nope"); }
catch (TypeKitAssertionException e) { Console.WriteLine($"assertion-failed={e.Message}"); }
