// sample: CodoMetis.TypeKit.Serilog/setup
using Serilog;

// end sample
using System.Text.RegularExpressions;
using ReadmeSamples.Generators.Masking;

namespace ReadmeSamples.Serilog;

[Collection(nameof(ConsoleOutput))]
public sealed partial class SerilogSamples
{
    private const string Readme = "src/CodoMetis.TypeKit.Serilog/README.md";

    /// <summary>The README's block, run with the console captured: the event says what its comment says.</summary>
    [Fact]
    public void The_client_is_logged_as_the_README_shows()
    {
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Run();
        }
        finally
        {
            Console.SetOut(original);
        }

        var shown = LoggedComment().Match(SampleOutputs.Readme(Readme));
        shown.Success.ShouldBeTrue($"{Readme} states no output in a comment.");
        output.ToString().ShouldContain(shown.Groups["output"].Value);
        output.ToString().ShouldNotContain("k-123");
    }

    private static void Run()
    {
        // sample: CodoMetis.TypeKit.Serilog/setup
        using var logger = new LoggerConfiguration()
            .Destructure.WithTypeKit()
            .WriteTo.Console()
            .CreateLogger();

        logger.Information("Created {@Client}", new Client(ApiKey.From("k-123"), "Ann"));   // Created {"Key": "ApiKey(***)", "Name": "Ann", "$type": "Client"}
        // end sample
    }

    [GeneratedRegex(@"\);\s+// (?<output>Created \{.*\})$", RegexOptions.Multiline)]
    private static partial Regex LoggedComment();
}

[CollectionDefinition(nameof(ConsoleOutput), DisableParallelization = true)]
public sealed class ConsoleOutput;
