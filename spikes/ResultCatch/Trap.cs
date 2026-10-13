#if TRAP
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;

namespace ResultCatch;

public static class Trap
{
    static Task<int> FetchAsync() => Task.FromResult(1);
    static Task DoAsync() => Task.CompletedTask;

    // Claim 5: each of these must be an error, not a Result over a Task.
    public static void AsyncTrap()
    {
        var a = Candidate.Catch(() => FetchAsync());
        var b = Candidate.Catch(() => DoAsync());
        var d = Candidate.Catch(FetchAsync);
    }

    // Claim 9: an exception is not a fault a client acts on.
    public static object HttpTrap() => Candidate.Catch(() => 1).ToHttpResult();
}
#endif
