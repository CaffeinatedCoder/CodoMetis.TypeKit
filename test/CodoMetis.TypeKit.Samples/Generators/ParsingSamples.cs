using System.Globalization;

namespace ReadmeSamples.Generators;

public static class ParsingAndFormatting
{
    public static void Use()
    {
        // sample: CodoMetis.TypeKit.Generators/parsing-and-formatting
        Quantity.Parse("3", null);                     // Quantity 3
        Quantity.TryParse("x", null, out _);           // false
        Email.Parse("nobody", null);                   // FormatException naming Email and NoAt

        var de     = CultureInfo.GetCultureInfo("de-DE");
        var text   = Amount.From(1.5m).ToString();                 // "1.5", whatever the current culture
        var german = Amount.From(1.5m).ToString("N2", de);         // "1,50"
        var shown  = $"{Amount.From(1.5m)}";                       // "1.5"
        // end sample
    }
}

public sealed class ParsingTests
{
    private const string Readme = "src/CodoMetis.TypeKit.Generators/README.md";

    [Fact]
    public void Parsing_is_what_the_comments_say()
    {
        Quantity.Parse("3", null).ShouldBeShownIn(Readme, quantity => $"{nameof(Quantity)} {quantity}");
        Quantity.TryParse("x", null, out _).ShouldBeShownIn(Readme, parsed => parsed ? "true" : "false");
        SampleOutputs.ShouldRefuseAsShownIn(() => Email.Parse("nobody", null), Readme, input: "nobody");
    }

    /// <summary>"Whatever the current culture": the current culture here is one that writes 1,5.</summary>
    [Fact]
    public void Formatting_is_what_the_comments_say()
    {
        var current = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            Amount.From(1.5m).ToString().ShouldBeShownIn(Readme, Quoted);
            Amount.From(1.5m).ToString("N2", de).ShouldBeShownIn(Readme, Quoted);
            $"{Amount.From(1.5m)}".ShouldBeShownIn(Readme, Quoted);
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
        }
    }

    private static string Quoted(string text) => $"\"{text}\"";
}
