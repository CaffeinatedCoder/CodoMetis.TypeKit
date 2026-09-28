using System.Text.Json;
using CodoMetis.TypeKit.CompilerServices;

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

        GeneratedFactories.OrJsonException(Result<Code, Fault>.Success(code)).ShouldBe(code);
        GeneratedFactories.OrFormatException(Result<Code, Fault>.Success(code)).ShouldBe(code);
    }

    [Fact]
    public void A_JSON_refusal_is_a_JsonException_naming_the_type_and_the_fault() =>
        Should.Throw<JsonException>(() => GeneratedFactories.OrJsonException(Result<Code, Fault>.Error(Fault.TooLong)))
              .Message.ShouldBe("Code refused the JSON value (TooLong). See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#a-value-object-refused-a-value");

    [Fact]
    public void A_parse_refusal_is_a_FormatException_naming_the_type_and_the_fault() =>
        Should.Throw<FormatException>(() => GeneratedFactories.OrFormatException(Result<Code, Fault>.Error(Fault.TooLong)))
              .Message.ShouldBe("Code refused the input (TooLong). See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#a-value-object-refused-a-value");

    /// <summary><c>bool</c> implements its parsing interfaces explicitly; <c>bool.Parse(s, provider)</c> does not compile.</summary>
    [Fact]
    public void An_explicitly_implemented_IParsable_is_reachable()
    {
        GeneratedParsing.TryParse<bool>("true", null, out var flag).ShouldBeTrue();
        flag.ShouldBeTrue();
        GeneratedParsing.TryParse<bool>("nope", null, out _).ShouldBeFalse();
        GeneratedParsing.TryParseSpan<char>("x".AsSpan(), null, out var character).ShouldBeTrue();
        character.ShouldBe('x');
        GeneratedParsing.TryParseUtf8<int>("42"u8, null, out var number).ShouldBeTrue();
        number.ShouldBe(42);
        GeneratedParsing.TryParseUtf8<int>("x"u8, null, out _).ShouldBeFalse();
    }

    /// <summary>What every generated <c>Parse</c> throws for text the wrapped type cannot read: both types, never the input.</summary>
    [Fact]
    public void Unreadable_input_names_the_types_and_nothing_else()
    {
        var exception = GeneratedParsing.Unreadable<Code, int>();

        exception.Message.ShouldBe("Code could not read the input as Int32. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#a-value-object-could-not-read-the-input");
        exception.InnerException.ShouldBeNull();
    }

    /// <summary>A parser's own exception quotes the input, so it is replaced, and not kept as the inner exception.</summary>
    [Fact]
    public void A_parser_s_exception_is_replaced_whole()
    {
        var guarded = Should.Throw<FormatException>(() => GeneratedParsing.Guarded<Code, int>("SECRET", static s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)));
        var converted = Should.Throw<FormatException>(() => GeneratedParsing.ConvertFromString<Code, int>("SECRET", null));

        foreach (var exception in new[] { guarded, converted })
        {
            exception.Message.ShouldBe("Code could not read the input as Int32. See https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/blob/main/src/CodoMetis.TypeKit/README.md#a-value-object-could-not-read-the-input");
            exception.InnerException.ShouldBeNull();
        }

        GeneratedParsing.Guarded<Code, int>("42", static s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(42);
        GeneratedParsing.ConvertFromString<Code, int>("42", null).ShouldBe(42);
    }
}
