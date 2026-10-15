namespace CodoMetis.TypeKit.Facts;

/// <summary>
/// design/knowledge/KB-004: on Windows, a rename that replaces a file another handle holds open is refused,
/// even when that handle allows <see cref="FileShare.Delete"/>, while moving the held file aside is allowed.
/// On Linux and macOS the replace succeeds and the reader keeps the old content.
/// </summary>
public sealed class KB004 : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "typekit-facts-kb004", Guid.NewGuid().ToString("N"));

    public KB004() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_replace_over_a_held_file_is_refused_on_Windows_and_moving_it_aside_is_not()
    {
        var target    = Path.Combine(_directory, "target");
        var temporary = Path.Combine(_directory, "target.tmp");
        var aside     = Path.Combine(_directory, "target.old");
        File.WriteAllText(target, "old");
        File.WriteAllText(temporary, "new");

        using var reader = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (OperatingSystem.IsWindows())
        {
            Should.Throw<UnauthorizedAccessException>(() => File.Move(temporary, target, overwrite: true), "Windows refuses the replace, whatever the reader's share");

            File.Move(target, aside);
            File.Move(temporary, target, overwrite: true);
        }
        else
        {
            File.Move(temporary, target, overwrite: true);
        }

        File.ReadAllText(target).ShouldBe("new");
        new StreamReader(reader).ReadToEnd().ShouldBe("old", "the reader keeps the file it opened");
    }
}
