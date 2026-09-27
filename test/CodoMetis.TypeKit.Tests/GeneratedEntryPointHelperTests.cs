using System.Text.Json;
using CodoMetis.TypeKit.ValueObjects;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// The run-time helpers the generated JSON converter and parsing call, tested without a generator:
/// what a refusal throws and says, and that an explicitly implemented <c>IParsable</c> is reachable.
/// </summary>
public sealed class GeneratedEntryPointHelperTests
{
    private enum Fault { TooLong }

    private readonly record struct Code;

    [Fact]
    public void An_accepted_value_is_unwrapped_by_both()
    {
        var code = new Code();

        Accepted.OrJsonException(Result<Code, Fault>.Success(code)).ShouldBe(code);
        Accepted.OrFormatException(Result<Code, Fault>.Success(code)).ShouldBe(code);
    }

    [Fact]
    public void A_JSON_refusal_is_a_JsonException_naming_the_type_and_the_fault() =>
        Should.Throw<JsonException>(() => Accepted.OrJsonException(Result<Code, Fault>.Error(Fault.TooLong)))
              .Message.ShouldBe("Code refused the JSON value (TooLong).");

    [Fact]
    public void A_parse_refusal_is_a_FormatException_naming_the_type_and_the_fault() =>
        Should.Throw<FormatException>(() => Accepted.OrFormatException(Result<Code, Fault>.Error(Fault.TooLong)))
              .Message.ShouldBe("Code refused the input (TooLong).");

    /// <summary><c>bool</c> implements its parsing interfaces explicitly; <c>bool.Parse(s, provider)</c> does not compile.</summary>
    [Fact]
    public void An_explicitly_implemented_IParsable_is_reachable()
    {
        GeneratedParsing.Parse<bool>("true", null).ShouldBeTrue();
        GeneratedParsing.TryParse<bool>("nope", null, out _).ShouldBeFalse();
        GeneratedParsing.ParseSpan<bool>("False".AsSpan(), null).ShouldBeFalse();
        GeneratedParsing.TryParseSpan<char>("x".AsSpan(), null, out var character).ShouldBeTrue();
        character.ShouldBe('x');
        GeneratedParsing.ParseUtf8<int>("42"u8, null).ShouldBe(42);
        GeneratedParsing.TryParseUtf8<int>("x"u8, null, out _).ShouldBeFalse();
    }
}
