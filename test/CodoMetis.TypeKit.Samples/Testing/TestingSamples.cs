// sample: CodoMetis.TypeKit.Testing/assertions
using CodoMetis.TypeKit.Testing;

// end sample
using System.Text.Json;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit;
using ReadmeSamples.Root.QuickStart;

namespace ReadmeSamples.Testing;

public sealed record OrderPatch(Patch<string> Name, Patch<string> Nickname, Patch<int> Quantity);

public sealed partial class TestingSamples
{
    private const string Readme = "src/CodoMetis.TypeKit.Testing/README.md";

    /// <summary>The README's block, run: each line passes, or this test fails with its assertion.</summary>
    [Fact]
    public void The_assertions_pass_as_the_README_shows()
    {
        // sample: CodoMetis.TypeKit.Testing/assertions
        Email email = Email.Create("ops@example.com").ShouldBeSuccess();   // the value, for the test's own assertions
        Email.Create("nobody").ShouldBeError(EmailFault.NoAt);              // this error, or the test fails
        EmailFault fault = Email.Create("").ShouldBeError();                // any error, handed back

        Email.TryFrom("ops@example.com").ShouldBeSome();
        Email.TryFrom("nobody").ShouldBeNone();
        // end sample

        email.Value.ShouldBe("ops@example.com");
        fault.ShouldBe(EmailFault.Blank);
    }

    /// <summary>The README's patch block, run: each line passes, or this test fails with its assertion.</summary>
    [Fact]
    public void The_patch_assertions_pass_as_the_README_shows()
    {
        // sample: CodoMetis.TypeKit.Testing/patch
        OrderPatch patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;

        int quantity = patch.Quantity.ShouldBeValue();   // the value, handed back
        patch.Nickname.ShouldBeNull();                   // a clear
        patch.Name.ShouldBeAbsent();                     // not in the document
        // end sample

        quantity.ShouldBe(2);
    }

    /// <summary>The failure message the README quotes, read from the README, is the one the assertion throws.</summary>
    [Fact]
    public void A_failure_says_what_the_README_quotes()
    {
        var quoted = QuotedFailure().Match(SampleOutputs.Readme(Readme));
        quoted.Success.ShouldBeTrue($"{Readme} quotes no failure message.");

        Should.Throw<TypeKitAssertionException>(() => Email.Create("nobody").ShouldBeSuccess())
              .Message.ShouldBe(Regex.Replace(quoted.Groups["message"].Value, @"\s+", " "));
    }

    [GeneratedRegex(@"`(?<message>Expected a success, but the[^`]+)`")]
    private static partial Regex QuotedFailure();
}
