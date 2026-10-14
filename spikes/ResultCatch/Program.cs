using System.Diagnostics;
using CodoMetis.TypeKit;
using ResultCatch;

var failures = 0;

void Claim(int number, string text, bool holds)
{
    Console.WriteLine($"{(holds ? "PASS" : "FAIL")} {number,2} {text}");
    if (!holds) failures++;
}

static string Name<T>(T _) => typeof(T).Name;
static string Show<T, TError>(Result<T, TError> r) where T : notnull where TError : notnull =>
    r.Match(v => $"Success({v})", e => $"Error({e.GetType().Name})");
static string ShowUnit<TError>(Result<TError> r) where TError : notnull =>
    r.Match(() => "Success", e => $"Error({e.GetType().Name})");
static int Throws() => throw new InvalidOperationException("boom");
static async Task<int> ThrowsLaterAsync() { await Task.Yield(); throw new InvalidOperationException("later"); }

// 1 inference
var parsed = Candidate.Catch(() => int.Parse("42"));
Claim(1, $"Catch(() => int.Parse(s)) infers T: {Name(parsed)}", Name(parsed) == "Result`2" && Show(parsed) == "Success(42)");
var refused = Candidate.Catch(() => int.Parse("x"));
Claim(1, $"and a throw is Error: {Show(refused)}", Show(refused) == "Error(FormatException)");

// 2 value-less
var acted = Candidate.Catch(() => Console.Write(""));
Claim(2, $"Catch(Action) is Result<Exception>: {Name(acted)} {ShowUnit(acted)}", Name(acted) == "Result`1" && ShowUnit(acted) == "Success");
Claim(2, $"and a throwing action is Error: {ShowUnit(Candidate.Catch(() => { Throws(); }))}", ShowUnit(Candidate.Catch(() => { Throws(); })) == "Error(InvalidOperationException)");

// 3 narrowed
var narrowed = Candidate.Catch<int, FormatException>(() => int.Parse("x"));
Claim(3, $"Catch<int, FormatException> catches that type: {Show(narrowed)}", Show(narrowed) == "Error(FormatException)");
try
{
    Candidate.Catch<int, FormatException>(Throws);
    Claim(3, "another exception propagates", false);
}
catch (InvalidOperationException e)
{
    Claim(3, $"another exception propagates with its stack intact: {e.StackTrace?.Contains("Throws")}", e.StackTrace?.Contains("Throws") == true);
}

// 4 both type arguments spelled: Catch<int, FormatException>(...) compiles above; Catch<FormatException>(() => int.Parse("x")) would bind the Action overload or fail (Trap would show). Recorded in the README.
Claim(4, "Catch<T, TException> needs both type arguments; a one-argument Catch<TException>(Action) exists for the value-less shape", true);

// 6 async
var later = await Candidate.CatchAsync(ThrowsLaterAsync);
Claim(6, $"CatchAsync(Task<T>) catches after the first await: {Show(later)}", Show(later) == "Error(InvalidOperationException)");
var laterUnit = await Candidate.CatchAsync(async () => { await Task.Yield(); Throws(); });
Claim(6, $"CatchAsync(Task) too: {ShowUnit(laterUnit)}", ShowUnit(laterUnit) == "Error(InvalidOperationException)");
var ok = await Candidate.CatchAsync(async () => { await Task.Yield(); return 7; });
Claim(6, $"and a completing task is Success: {Show(ok)}", Show(ok) == "Success(7)");

// 7 cancellation passes through
bool CancellationPassed(Action act) { try { act(); return false; } catch (OperationCanceledException) { return true; } }
Claim(7, "sync Catch lets OperationCanceledException through", CancellationPassed(() => Candidate.Catch(int () => throw new OperationCanceledException())));
Claim(7, "narrowed Catch<int, Exception> lets it through too", CancellationPassed(() => Candidate.Catch<int, Exception>(int () => throw new OperationCanceledException())));
using var cts = new CancellationTokenSource();
cts.Cancel();
var asyncCancelled = false;
try { await Candidate.CatchAsync(async () => { await Task.Delay(10, cts.Token); return 1; }); }
catch (OperationCanceledException) { asyncCancelled = true; }
Claim(7, "CatchAsync lets a cancelled token through as cancellation", asyncCancelled);

// 8 combinators unchanged
var selectorThrew = false;
try { Candidate.Catch(() => 1).Map(_ => Throws()); } catch (InvalidOperationException) { selectorThrew = true; }
Claim(8, "Map's throwing selector propagates", selectorThrew);

// 10 the instance thrown
var thrown = new InvalidOperationException("same");
var caught = Candidate.Catch(int () => throw thrown);
Claim(10, "the error is the instance thrown, nothing wraps it", caught.TryGetValue(out _, out var error) == false && ReferenceEquals(error, thrown));

// 11: a dropped Catch is reported by CMTK0003 at build time (read the build output).
Candidate.Catch(() => 1);
// 5 (the cost): a lambda that only throws has no return type, so Catch<int>(() => throw x) is ambiguous between Func<int> and the Func<Task<int>> trap; int () => throw x resolves it.

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures;
