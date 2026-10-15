using System.Text;

/// <summary>
/// How every tool reads a file another process may replace, and replaces a file another process may be
/// reading. On Linux and macOS a rename over an open file just works. On Windows it fails twice over: a
/// reader that did not allow <see cref="FileShare.Delete"/> blocks it, and even a reader that did blocks a
/// rename that <em>replaces</em> the file, though not one that moves it aside. So readers here always allow
/// delete, and <see cref="Replace"/> falls back to moving the old file aside first (design/adr/0092).
/// </summary>
internal static class SharedFile
{
    private const FileShare Shared = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Opens for reading, letting a writer rename a new file over this one while it is open.</summary>
    public static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, Shared);

    public static string ReadAllText(string path)
    {
        using var reader = new StreamReader(OpenRead(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static string[] ReadAllLines(string path)
    {
        var lines = new List<string>();
        using var reader = new StreamReader(OpenRead(path), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line) lines.Add(line);
        return [.. lines];
    }

    public static byte[] ReadAllBytes(string path)
    {
        using var stream = OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// Moves <paramref name="temporary"/> over <paramref name="target"/>: a reader sees the old file or the new,
    /// never half of one. Where the replace is refused (Windows, a reader holding the target), the target is
    /// moved aside, the new file moved into its place, and the old one deleted, which completes once its last
    /// reader closes it. A few attempts cover a writer racing this one; then the refusal is thrown.
    /// </summary>
    public static void Replace(string temporary, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                return;
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                var aside = $"{target}.{Environment.ProcessId}.{Guid.NewGuid():N}.old";
                try
                {
                    if (File.Exists(target)) File.Move(target, aside);
                    File.Move(temporary, target, overwrite: true);
                    TryDelete(aside);
                    return;
                }
                catch (Exception again) when ((again is IOException or UnauthorizedAccessException) && attempt < 5)
                {
                    // The old file goes back where it was, never lost: a new one is in place, or the old one is.
                    if (File.Exists(aside))
                    {
                        if (File.Exists(target)) TryDelete(aside);
                        else TryMove(aside, target);
                    }
                    Thread.Sleep(Random.Shared.Next(10, 50));
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryMove(string from, string to)
    {
        try { File.Move(from, to); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
