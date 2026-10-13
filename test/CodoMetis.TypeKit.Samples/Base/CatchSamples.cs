using CodoMetis.TypeKit;

namespace ReadmeSamples.Base.Catching;

public static class Outcomes
{
    public static async Task RunAsync(string source, string target, HttpClient client, Uri url)
    {
        // sample: CodoMetis.TypeKit/catch
        Result<int, Exception> parsed = Result.Catch(() => int.Parse("42"));                                   // Success: 42
        Result<int, Exception> refused = Result.Catch(() => int.Parse("x"));                                   // Error: FormatException
        Result<int, FormatException> narrowed = Result.Catch<int, FormatException>(() => int.Parse("x"));     // only that type; any other propagates
        Result<Exception> copied = Result.Catch(() => File.Copy(source, target));                             // a command: a success, or the exception
        Result<byte[], Exception> fetched = await Result.CatchAsync(() => client.GetByteArrayAsync(url));      // waits for the task; its exception is the error
        // end sample

        _ = (parsed, refused, narrowed, copied, fetched);
    }
}

public sealed class CatchTests
{
    private const string Readme = "src/CodoMetis.TypeKit/README.md";

    private static string Render<T>(Result<T, Exception> result) where T : notnull =>
        result.Match(value => $"Success: {value}", error => $"Error: {error.GetType().Name}");

    [Fact]
    public void Each_line_answers_what_its_comment_states()
    {
        Result.Catch(() => int.Parse("42")).ShouldBeShownIn(Readme, Render);
        Result.Catch(() => int.Parse("x")).ShouldBeShownIn(Readme, Render);
    }

    [Fact]
    public async Task The_sample_runs_as_written()
    {
        var source = Path.GetTempFileName();
        var target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMilliseconds(100) };
            await Outcomes.RunAsync(source, target, client, new Uri("http://127.0.0.1:1/unreachable"));
            File.Exists(target).ShouldBeTrue();
        }
        finally
        {
            File.Delete(source);
            File.Delete(target);
        }
    }
}
