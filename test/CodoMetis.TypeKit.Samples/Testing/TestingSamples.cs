// sample: CodoMetis.TypeKit.Testing/assertions
using CodoMetis.TypeKit.Testing;

// end sample
using System.Text.Json;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit;
using ReadmeSamples.Root.QuickStart;

namespace ReadmeSamples.Testing;

public sealed record OrderPatch(Patch<string> Name, Patch<string> Nickname, Patch<int> Quantity);

public enum OrderFault { NotFound }

public enum EditFault { Stale }

/// <summary>A boundary answering with its own faults and a shared set's, for the composed fault's block.</summary>
public sealed class Orders
{
    public Result<Fault<OrderFault, EditFault>> Rename(int id, int version) =>
        Fault<OrderFault, EditFault>.Of(EditFault.Stale, $"Order {id} is at version {version + 1}, not {version}.");
}

/// <summary>A service answering through tasks, for the asynchronous block.</summary>
public sealed class Signups
{
    public async Task<Result<Email, EmailFault>> RegisterAsync(string address)
    {
        await Task.Yield();
        return Email.Create(address);
    }

    public ValueTask<Option<Email>> FindAsync(string address) => new(Option.None<Email>());
}

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

    /// <summary>The README's composed-fault block, run: the held fault comes back with its detail.</summary>
    [Fact]
    public void A_composed_fault_hands_back_the_fault_it_holds()
    {
        var orders = new Orders();

        // sample: CodoMetis.TypeKit.Testing/composed-fault
        Fault<OrderFault, EditFault> refusal = orders.Rename(7, version: 3).ShouldBeError();
        Fault<EditFault> stale = refusal.ShouldHold(EditFault.Stale);   // this fault, or the test fails
        string? detail = stale.Detail;                                  // for the test's own assertions
        // end sample

        detail.ShouldBe("Order 7 is at version 4, not 3.");
        Should.Throw<TypeKitAssertionException>(() => refusal.ShouldHold(OrderFault.NotFound));
    }

    /// <summary>The README's asynchronous block, run: each line awaits and passes, or this test fails with its assertion.</summary>
    [Fact]
    public async Task The_assertions_on_a_task_pass_as_the_README_shows()
    {
        var signups = new Signups();

        // sample: CodoMetis.TypeKit.Testing/async
        Email email = await signups.RegisterAsync("ops@example.com").ShouldBeSuccess();   // awaited, then asserted
        await signups.RegisterAsync("nobody").ShouldBeError(EmailFault.NoAt);
        await signups.FindAsync("ops@example.com").ShouldBeNone();                         // a ValueTask too
        // end sample

        email.Value.ShouldBe("ops@example.com");
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
