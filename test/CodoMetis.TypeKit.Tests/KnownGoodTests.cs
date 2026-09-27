using CodoMetis.TypeKit.CompilerServices;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The throw behind the generated <c>FromKnownGood</c>, tested without a generator: the message
/// names the type, the caller's expression and the fault, and never the refused value.
/// </summary>
public sealed class KnownGoodTests
{
    private enum Fault { TooLong }

    private readonly record struct Code;

    [Fact]
    public void An_accepted_value_is_unwrapped()
    {
        var code = new Code();

        GeneratedFactories.OrInvalidOperationException(Result<Code, Fault>.Success(code), "input").ShouldBe(code);
    }

    [Fact]
    public void A_refusal_names_the_type_the_expression_and_the_fault()
    {
        var exception = Should.Throw<InvalidOperationException>(() => GeneratedFactories.OrInvalidOperationException(Result<Code, Fault>.Error(Fault.TooLong), "request.Code"));

        exception.Message.ShouldBe("Code refused request.Code, which the call site declared known-good (TooLong).");
    }

    [Fact]
    public void Without_an_expression_the_message_still_names_the_type_and_the_fault()
    {
        var exception = Should.Throw<InvalidOperationException>(() => GeneratedFactories.OrInvalidOperationException(Result<Code, Fault>.Error(Fault.TooLong), null));

        exception.Message.ShouldBe("Code refused a value the call site declared known-good (TooLong).");
    }
}
