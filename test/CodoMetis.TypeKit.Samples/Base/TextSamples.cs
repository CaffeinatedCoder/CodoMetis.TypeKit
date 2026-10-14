using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace ReadmeSamples.Base.Text;

// sample: CodoMetis.TypeKit/text-value-object
public readonly partial record struct Description : IValidatedValue<Description, string, TextFault>
{
    public static Result<Description, TextFault> Create(string value) =>
        value.AsText().Map(text => new Description(text));
}
// end sample

public static class Guards
{
    public static (Result<string, TextFault> Name, Result<string, TextFault> Subject, Result<string, TextFault> Missing) Run()
    {
        // sample: CodoMetis.TypeKit/text
        var name    = "  Jean\u00A0Paul ".AsText();        // Success: Jean Paul, trimmed, the no-break space folded to an ordinary one
        var subject = "subject\u200Bid".AsIdentifier();   // Error: Hidden, a zero-width space
        var missing = ((string?)null).AsText();            // Error: Blank
        // end sample

        return (name, subject, missing);
    }
}

public sealed class TextTests
{
    private const string Readme = "src/CodoMetis.TypeKit/README.md";

    private static string Render(Result<string, TextFault> result) =>
        result.Match(text => $"Success: {text}", fault => $"Error: {fault}");

    [Fact]
    public void Each_guard_answers_what_its_comment_states()
    {
        "  Jean\u00A0Paul ".AsText().ShouldBeShownIn(Readme, Render);
        "subject\u200Bid".AsIdentifier().ShouldBeShownIn(Readme, Render);
        ((string?)null).AsText().ShouldBeShownIn(Readme, Render);

        var (name, subject, missing) = Guards.Run();
        Render(name).ShouldStartWith("Success: Jean Paul");
        Render(subject).ShouldBe("Error: Hidden");
        Render(missing).ShouldBe("Error: Blank");
    }

    [Fact]
    public void The_value_object_applies_the_guard()
    {
        Description.Create("  Jean\u00A0Paul ").Match(d => d.Value, _ => "").ShouldBe("Jean Paul");
        Description.TryFrom("\u200B").IsNone().ShouldBeTrue();
        Description.Create("Mu\u0308ller").ShouldBe(Description.Create("M\u00FCller"));
    }
}
