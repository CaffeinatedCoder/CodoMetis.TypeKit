namespace Prober;

internal static class Program
{
    private const string Usage =
        """
        prober: the probing workflow's core. No command starts a model; only `round` does, locally.

        Ledger (any checkout of the repository; --branch B, default the checked-out branch):
          universe [--base REF]             each claim's axes, from the claims, the axes file and the generated axes
          rows --claim C [--full]           a pairwise covering array over the claim's pairs not yet covered;
                                            inside a capped round it refuses past the deadline or the claim cap
          rows --probe P [--full]           writes P's rows file whole into the staging: a covering array over its
                                            claim's pairs less those the claim's other probes have credited
          status [--json]                   coverage from current evidence, open findings, done or not; per claim
                                            its obligations and which have a seed
          gaps [--base REF]                 what the corpus leaves out, each with its reason: unassigned settings,
                                            refused shapes and values, what claims do not promise, out of reach, and
                                            the wanted pairs no passing check of the last replay read both axes of
          gate [--since REF] [--json]       the release gate (needs only the tracked corpus and a replay)
          stale [--json] [--include-retired]   the probes a replay would run
          identity --probe P                what a result of P is bound to
          record-run --probe P --result pass|fail [--evidence F] [--seconds S]
          footprint --probe P --cobertura F... --root WT [--include src] [--compile-time PATH...]
          register --probe P --claim C --filter F [--variants JSON] [--seed PATCH] [--seed-env NAME=VALUE...] [--defect TEXT]
                                            [--obligation O]: the seed (a patch, a toggle or both) into `seed`, or with
                                            --obligation into `seeds`, in place of a seed naming O; refuses an O that
                                            claim C does not list
          publish | dismiss --id F --reason TEXT | mark retired --probe P
          fix F --commit SHA [--reason TEXT]   records open finding F fixed at the commit (findings.jsonl); refuses
                                            an id this ledger does not hold open and a commit the repository lacks
          lock --holder H --mode replay|explore|full [--pid N] | unlock --holder H | heartbeat --holder H
          round-start --holder H --minutes M [--claims N] | round-end --holder H | repair | abandon
          merge --from DIR...               joins the states of a replay split by family (one DIR per leg) into
                                            this one; refuses while a registered probe was in no leg

        Writing a probe (into the branch's staging):
          new --claim C [--probe P] [--family F]   stages P's skeleton (the claim's source and obligations quoted, one
                                            check per obligation that throws until written), the claim's rows and P's
                                            registry entry with no seed yet; P defaults to the claim's prefix and words
                                            (C20-testing-shouldhold: C20TestingShouldholdProbe); refuses a claim not in
                                            the claims, a name taken, a family that is not the claim's
          try --probe P [--rows i,j,...] [--no-build]   builds once in the replay's worktree and runs P alone, every
                                            variant or the rows given, printing how each row ended; records nothing,
                                            holds the worktree from a replay, exits with the probe's exit code

        Running probes:
          replay [--all] [--family F] [--parallel N] [--no-footprint] [--holder H] [--dry-run] [--where]
                                            replays the corpus with no model: the stale probes, or all; one family
                                            of the replay configuration; N probe processes at a time (default:
                                            the cores); without coverage or footprint, for a state nobody keeps
          round explore|full [--minutes M] [--claims N] [--usd X]   a capped exploration round (local only)
          seeds [--all] [--probe P] [--dry-run]   applies each probe's seeded defect in a scratch worktree, replays the
                                            probe there and requires a violation it causes (local only, a build per
                                            seed): the probes whose identity or footprint changed since every seed of
                                            theirs was caught, or all of them, as a release runs it

        State goes under PROBE_STATE_DIR when set (CI), else the main checkout's prober memory.
        Exit codes: 0 done, 1 refused or failed, 2 usage, 3 this dll is stale; `try` exits with the probe's own.
        """;

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal)
        { "json", "full", "all", "include-retired", "dry-run", "where", "no-footprint", "no-build" };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help") { Console.Write(Usage); return args.Length == 0 ? 2 : 0; }
        var home = FindRoot(AppContext.BaseDirectory);
        if (home is not null && SourceFingerprint.Difference(home) is { } stale)
        {
            Console.Error.WriteLine($"prober: this dll was built from other sources than the checkout holds ({stale}). Run `{SourceFingerprint.RebuildCommand}`.");
            return SourceFingerprint.StaleExitCode;
        }
        return Run(Context.FromProcess(), args);
    }

    /// <summary>Runs one command against a context: the process's, or a test's.</summary>
    public static int Run(Context ctx, string[] args)
    {
        try
        {
            var command = args[0];
            var a = new Args(args[1..], Flags);
            if (command == "replay") return Replay.Run(ctx, a);
            if (command == "round") return Round.Run(ctx, a);
            if (command == "seeds") return Seeds.Run(ctx, a);
            if (command == "try") return TryProbe.Run(ctx, a);
            var ledger = new Ledger(ctx, a.Get("branch"));
            // Commands run under a round keep its lock alive.
            if (command is not ("lock" or "unlock" or "abandon" or "heartbeat") && ctx.Env("PROBE_HOLDER") is { } holder)
                RoundLock.Beat(ledger, holder);
            return command switch
            {
                "universe" => Commands.UniverseCommand(ledger, a),
                "rows" => Commands.Rows(ledger, a),
                "status" => Commands.Status(ledger, a),
                "gate" => Commands.Gate(ledger, a),
                "stale" => Commands.Stale(ledger, a),
                "identity" => Commands.IdentityCommand(ledger, a),
                "record-run" => Commands.RecordRun(ledger, a),
                "footprint" => Commands.Footprint(ledger, a),
                "new" => NewProbe.Run(ledger, a),
                "register" => Commands.Register(ledger, a),
                "publish" => Commands.Publish(ledger, a),
                "dismiss" => Commands.Dismiss(ledger, a),
                "fix" => Commands.Fix(ledger, a),
                "mark" => Commands.Mark(ledger, a),
                "lock" => Commands.Lock(ledger, a),
                "unlock" => Commands.Unlock(ledger, a),
                "heartbeat" => Commands.HeartbeatCommand(ledger, a),
                "owe-replay" => Commands.OweReplay(ledger, a),
                "round-start" => Commands.RoundStart(ledger, a),
                "round-end" => Commands.RoundEnd(ledger, a),
                "repair" => Commands.Repair(ledger, a),
                "abandon" => Commands.Abandon(ledger, a),
                "merge" => Commands.Merge(ledger, a),
                "gaps" => Commands.Gaps(ledger, a),
                _ => throw new Refusal($"unknown command {command}; see `prober help`", 2),
            };
        }
        catch (Refusal refusal)
        {
            ctx.Error.WriteLine("prober: " + refusal.Message);
            return refusal.ExitCode;
        }
    }

    private static string? FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CodoMetis.TypeKit.slnx"))) return dir.FullName;
        return null;
    }
}
