using System.Text.Json.Nodes;

namespace Prober;

/// <summary>
/// One round at a time. The lock file is created exclusively, with its holder's owner and a heartbeat
/// inside; a holder is alive while its heartbeat is younger than <see cref="HeartbeatMinutes"/>. Liveness is
/// the heartbeat alone (no process is probed, no signal sent), which is the same on every OS. Every
/// read-judge-write of the lock runs under an exclusive file create (the mutex), so two takers can never both see
/// it free, or both take over a holder whose heartbeat stopped.
/// </summary>
internal static class RoundLock
{
    public const int HeartbeatMinutes = 20;
    public static readonly TimeSpan BeatEvery = TimeSpan.FromSeconds(30);
    private const int MutexStaleSeconds = 60;

    public static string LockPath(Ledger l) => Path.Combine(l.Memory, "round.lock");

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static bool Alive(JsonObject held) =>
        Now() - Files.Num(held["heartbeat"], Files.Num(held["since"])) < HeartbeatMinutes * 60;

    public static JsonObject? Holder(Ledger l) => Files.ReadJson(LockPath(l)) is JsonObject held && Alive(held) ? held : null;

    /// <summary>
    /// The mutex: a file created with <see cref="FileMode.CreateNew"/>, which fails when it exists, atomically
    /// on every OS. A crashed writer's mutex goes stale after a minute and is removed.
    /// </summary>
    public static T WithMutex<T>(Ledger l, Func<T> body)
    {
        Directory.CreateDirectory(l.Memory);
        var path = LockPath(l) + ".mutex";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                break;
            }
            catch (Exception busy) when (busy is IOException or UnauthorizedAccessException)
            {
                // It exists (or vanished an instant ago, when its holder released it): either way, look again.
                // Windows says UnauthorizedAccess, not IOException, for a name whose file is being deleted.
                try
                {
                    if (File.Exists(path) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds > MutexStaleSeconds) File.Delete(path);
                }
                catch (Exception gone) when (gone is IOException or UnauthorizedAccessException) { }
                if (DateTime.UtcNow > deadline) throw new Refusal("the lock's mutex is held for over 10 s; another command is stuck");
                Thread.Sleep(Random.Shared.Next(5, 25));
            }
        }
        try { return body(); }
        finally { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    public static void WithMutex(Ledger l, Action body) => WithMutex(l, () => { body(); return 0; });

    public static (bool Taken, JsonObject? Previous) Take(Ledger l, string holder, string mode, long? pid)
    {
        var now = Now();
        var record = new JsonObject
        {
            ["schemaVersion"] = Files.SchemaVersion, ["holder"] = holder, ["mode"] = mode,
            ["owner"] = new JsonObject { ["pid"] = pid ?? Environment.ProcessId, ["host"] = Environment.MachineName },
            ["since"] = now, ["heartbeat"] = now, ["pending_replay"] = false,
        };
        return WithMutex(l, () =>
        {
            var held = Files.ReadJson(LockPath(l)) as JsonObject;
            if (held is not null && Files.Str(held["holder"]) != holder && Alive(held)) return (false, held);
            if (held is not null)
            {
                record["pending_replay"] = held["pending_replay"] is JsonValue p && p.TryGetValue<bool>(out var b) && b;
                Files.WriteJson(LockPath(l), record);   // a holder taking it again, or one whose heartbeat stopped
            }
            else
            {
                // No lock: an exclusive create, so even a writer that bypasses the mutex cannot overwrite one.
                using var stream = new FileStream(LockPath(l), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream);
                writer.Write(record.ToJsonString());
            }
            return (true, held);
        });
    }

    public static void Beat(Ledger l, string holder) => WithMutex(l, () =>
    {
        if (Files.ReadJson(LockPath(l)) is JsonObject held && Files.Str(held["holder"]) == holder)
        {
            held["heartbeat"] = Now();
            Files.WriteJson(LockPath(l), held);
        }
    });

    public static void OweReplay(Ledger l) => WithMutex(l, () =>
    {
        if (Files.ReadJson(LockPath(l)) is JsonObject held)
        {
            held["pending_replay"] = true;
            Files.WriteJson(LockPath(l), held);
        }
    });

    /// <summary>Releases the lock; returns whether a replay was owed. Refuses another holder's lock.</summary>
    public static bool Release(Ledger l, string holder) => WithMutex(l, () =>
    {
        var held = Files.ReadJson(LockPath(l)) as JsonObject;
        if (held is not null && Files.Str(held["holder"]) != holder)
            throw new Refusal($"the lock is {Files.Str(held["holder"])}'s, not {holder}'s");
        if (held is not null) File.Delete(LockPath(l));
        return held?["pending_replay"] is JsonValue p && p.TryGetValue<bool>(out var b) && b;
    });

    public static JsonObject? Abandon(Ledger l) => WithMutex(l, () =>
    {
        var held = Files.ReadJson(LockPath(l)) as JsonObject;
        if (held is not null) File.Delete(LockPath(l));
        return held;
    });

    /// <summary>Keeps a lock alive while a long step runs: a timer beats every 30 s.</summary>
    public sealed class Heartbeat(Ledger ledger, string holder) : IDisposable
    {
        private readonly Timer timer = new(_ => { try { Beat(ledger, holder); } catch (Exception) { } }, null, BeatEvery, BeatEvery);
        public void Dispose() => timer.Dispose();
    }
}
