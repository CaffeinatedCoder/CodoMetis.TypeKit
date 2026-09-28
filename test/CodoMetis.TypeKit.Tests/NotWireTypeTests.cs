using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>Option</c>, both <c>Result</c> shapes and the <c>Result.Ok</c>/<c>Result.Error</c> markers refuse
/// System.Text.Json in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Their content is in private fields, so without a converter the serializer writes <c>{}</c> for a
/// <c>Some</c> and <c>{"State":1}</c> for a success, and reads both back as <c>default</c>: a
/// <c>None</c>, or an uninitialized result. Nothing raises. The guard is that every such type carries
/// the refusing converter, that the refusal names the type and never the content, and that an
/// application can still override it on its own options.
/// </para>
/// <para>
/// The completeness test walks every exported struct of the assembly, so a struct added later
/// refuses serialization too, or is exempted here on purpose.
/// </para>
/// </remarks>
public sealed partial class NotWireTypeTests
{
    private const string Secret = "s3cr3t-content";

    private static readonly Dictionary<string, (object Instance, string TypeName, string Advice)> Cases = new()
    {
        ["Some"]                = (Option.Some(Secret), "Option<String>", "String?"),
        ["None"]                = (Option.None<string>(), "Option<String>", "ToOption() and OrNull()"),
        ["error-only success"]  = (Result<string>.Success(), "Result<String>", "Match it"),
        ["error-only error"]    = (Result<string>.Error(Secret), "Result<String>", "Match it"),
        ["valued success"]      = (Result<string, string>.Success(Secret), "Result<String, String>", "Match it"),
        ["valued error"]        = (Result<string, string>.Error(Secret), "Result<String, String>", "Match it"),
        ["None() marker"]       = (Option.None(), "None", "ToOption() and OrNull()"),
        ["Success() marker"]    = (Result.Success(), "Success", "Match it"),
        ["Success(value) marker"] = (Result.Success(Secret), "Success<String>", "Match it"),
        ["Error(error) marker"] = (Result.Error(Secret), "Error<String>", "Match it"),
    };

    public static TheoryData<string> CaseNames => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Serializing_throws_and_names_the_type_and_the_alternative_but_never_the_content(string @case)
    {
        var (instance, typeName, advice) = Cases[@case];

        var exception = Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(instance, instance.GetType()));

        exception.Message.ShouldStartWith(typeName);
        exception.Message.ShouldContain("wire type");
        exception.Message.ShouldContain(advice);
        exception.Message.ShouldContain("register a converter");
        exception.Message.ShouldNotContain(Secret);
    }

    public static TheoryData<string> OptionKinds => ["int", "Guid", "string", "Uri"];

    /// <summary>
    /// The refusal's advice compiles for an option of either kind: the nullable it names, and calls it
    /// names that convert that nullable into the option and back. It advised <c>OrNull()</c> for an
    /// <c>Option&lt;int&gt;</c>, which existed only for a reference type (CS0452). The advice is read
    /// from the message and compiled as a consumer would write it, so the message and the API cannot
    /// drift apart.
    /// </summary>
    [Theory]
    [MemberData(nameof(OptionKinds))]
    public void The_advice_for_an_option_compiles_for_its_kind(string kind)
    {
        var (instance, type) = kind switch
        {
            "int"    => ((object)Option.Some(5), typeof(int)),
            "Guid"   => (Option.Some(Guid.Empty), typeof(Guid)),
            "string" => (Option.Some(Secret), typeof(string)),
            _        => (Option.Some(new Uri("https://example.org")), typeof(Uri)),
        };

        var message = Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(instance, instance.GetType())).Message;
        var advice = Advice().Match(message);
        advice.Success.ShouldBeTrue($"The refusal names no nullable and no calls: {message}");
        advice.Groups["nullable"].Value.ShouldBe($"{type.Name}?");

        var calls = AdvisedCall().Matches(advice.Groups["calls"].Value).Select(match => match.Groups["name"].Value).ToList();
        calls.Count.ShouldBeGreaterThanOrEqualTo(2, message);

        var option = $"Option<global::{type.FullName}>";
        var nullable = $"global::{type.FullName}?";
        var outward = calls.ToDictionary(call => call, call => ConsumerCompilation.Of(Consumer($"{nullable} Out({option} option) => option.{call}();")).Problems());
        var inward = calls.ToDictionary(call => call, call => ConsumerCompilation.Of(Consumer($"{option} In({nullable} nullable) => nullable.{call}();")).Problems());

        foreach (var call in calls)
            (outward[call].Length == 0 || inward[call].Length == 0).ShouldBeTrue(
                $"{call}() is advised for {option} but converts it neither way:{Environment.NewLine}{outward[call]}{Environment.NewLine}{inward[call]}");

        outward.Values.ShouldContain(problems => problems.Length == 0, $"No advised call turns {option} into {nullable}:{Environment.NewLine}{string.Join(Environment.NewLine, outward.Values)}");
        inward.Values.ShouldContain(problems => problems.Length == 0, $"No advised call turns {nullable} into {option}:{Environment.NewLine}{string.Join(Environment.NewLine, inward.Values)}");
    }

    private static string Consumer(string member) =>
        $$"""
          using CodoMetis.TypeKit;

          public static class Consumer
          {
              public static {{member}}
          }
          """;

    [GeneratedRegex(@"a nullable \((?<nullable>[^)]+)\), and (?<calls>.+?) convert at the boundary")]
    private static partial Regex Advice();

    [GeneratedRegex(@"(?<name>\w+)\(\)")]
    private static partial Regex AdvisedCall();

    public static TheoryData<string, string> TypeNameAndJson =>
    [
        .. from @case in Cases.Keys.Distinct()
           from json in new[] { "{}", "null", "\"text\"", "5", "{\"State\":1}" }
           select (@case, json),
    ];

    [Theory]
    [MemberData(nameof(TypeNameAndJson))]
    public void Deserializing_throws_whatever_the_token(string @case, string json)
    {
        var type = Cases[@case].Instance.GetType();

        var exception = Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize(json, type));

        exception.Message.ShouldStartWith(Cases[@case].TypeName);
    }

    [Fact]
    public void A_shape_with_an_option_or_result_property_cannot_be_written()
    {
        var shape = new Shape(Option.Some(Secret), Result<int, string>.Success(1));

        var exception = Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(shape));

        exception.Message.ShouldContain("Option<String>");
        exception.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public void A_shape_whose_option_property_is_present_cannot_be_read()
    {
        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<Shape>("""{"Nickname":{},"Outcome":{"State":1}}"""))
              .Message.ShouldContain("Option<String>");
    }

    /// <summary>
    /// A nullable of each refused type, <c>Option&lt;string&gt;?</c> in a PATCH-style shape, is refused
    /// as the type itself is, whether it holds a value or not: the serializer's converter for
    /// <c>Nullable&lt;T&gt;</c> is the refusing converter itself. Writing a null threw a
    /// <see cref="NullReferenceException"/> from the refusal, and every read of an option gave the
    /// advice for a result ("Nullable&lt;Option&lt;String&gt;&gt; is an outcome").
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void A_nullable_of_it_is_refused_with_its_own_message_whether_null_or_not(string @case)
    {
        var (instance, typeName, advice) = Cases[@case];
        var nullable = typeof(Nullable<>).MakeGenericType(instance.GetType());

        foreach (var value in new[] { null, instance })
            ShouldRefuse(() => JsonSerializer.Serialize(value, nullable), typeName, advice, $"{@case}, writing {value ?? "null"}");

        foreach (var json in new[] { "null", "{}", "5" })
            ShouldRefuse(() => JsonSerializer.Deserialize(json, nullable), typeName, advice, $"{@case}, reading {json}");
    }

    [Fact]
    public void A_shape_with_a_nullable_option_or_result_property_is_refused_whether_it_is_null_or_not()
    {
        ShouldRefuse(() => JsonSerializer.Serialize(new NullableNickname(null)), "Option<String>", "String?", "writing a null option");
        ShouldRefuse(() => JsonSerializer.Serialize(new NullableNickname(Option.Some(Secret))), "Option<String>", "String?", "writing an option");
        ShouldRefuse(() => JsonSerializer.Deserialize<NullableNickname>("""{"Nickname":null}"""), "Option<String>", "String?", "reading a null option");
        ShouldRefuse(() => JsonSerializer.Deserialize<NullableNickname>("""{"Nickname":"Ric"}"""), "Option<String>", "String?", "reading an option");

        ShouldRefuse(() => JsonSerializer.Serialize(new NullableOutcome(null)), "Result<Int32, String>", "Match it", "writing a null result");
        ShouldRefuse(() => JsonSerializer.Serialize(new NullableOutcome(Result<int, string>.Error(Secret))), "Result<Int32, String>", "Match it", "writing a result");
        ShouldRefuse(() => JsonSerializer.Deserialize<NullableOutcome>("""{"Outcome":null}"""), "Result<Int32, String>", "Match it", "reading a null result");
        ShouldRefuse(() => JsonSerializer.Deserialize<NullableOutcome>("""{"Outcome":{"State":1}}"""), "Result<Int32, String>", "Match it", "reading a result");
    }

    /// <summary>
    /// Pins what the README states: a source-generated context cannot build the contract of a shape
    /// with an <c>Option&lt;T&gt;?</c> or a <c>Result&lt;…&gt;?</c> property, and fails before any
    /// document is read or written, with the serializer's own message naming the type.
    /// </summary>
    /// <remarks>
    /// The serializer's source-generated contract for <c>T?</c> takes the converter of <c>T</c> only as a
    /// <c>JsonConverter&lt;T&gt;</c> (<c>JsonMetadataServices.GetNullableConverter&lt;T&gt;</c>), and the
    /// refusal is one converter typed <see cref="object"/>: a converter per type needs
    /// <c>MakeGenericType</c>, which Native AOT cannot honour for these structs. It would not help
    /// either: that contract writes and reads a null itself without asking the converter of
    /// <c>T</c>, so a null would pass silently (measured 2026-09-28 with a typed refusing converter).
    /// If this starts to pass, the serializer changed, and the README's paragraph with it.
    /// </remarks>
    [Fact]
    public void A_source_generated_context_cannot_build_the_contract_of_a_nullable_option_or_result()
    {
        var attempts = new (string Case, Action Attempt, string Type)[]
        {
            ("writing a null option", () => JsonSerializer.Serialize(new NullableNickname(null), NullableContext.Default.NullableNickname), "CodoMetis.TypeKit.Option`1[System.String]"),
            ("writing an option", () => JsonSerializer.Serialize(new NullableNickname(Option.Some(Secret)), NullableContext.Default.NullableNickname), "CodoMetis.TypeKit.Option`1[System.String]"),
            ("reading a null option", () => JsonSerializer.Deserialize("""{"Nickname":null}""", NullableContext.Default.NullableNickname), "CodoMetis.TypeKit.Option`1[System.String]"),
            ("reading an option", () => JsonSerializer.Deserialize("""{"Nickname":"Ric"}""", NullableContext.Default.NullableNickname), "CodoMetis.TypeKit.Option`1[System.String]"),
            ("reading no option", () => JsonSerializer.Deserialize("{}", NullableContext.Default.NullableNickname), "CodoMetis.TypeKit.Option`1[System.String]"),
            ("writing a null result", () => JsonSerializer.Serialize(new NullableOutcome(null), NullableContext.Default.NullableOutcome), "CodoMetis.TypeKit.Result`2[System.Int32,System.String]"),
            ("writing a result", () => JsonSerializer.Serialize(new NullableOutcome(Result<int, string>.Success(1)), NullableContext.Default.NullableOutcome), "CodoMetis.TypeKit.Result`2[System.Int32,System.String]"),
            ("reading a null result", () => JsonSerializer.Deserialize("""{"Outcome":null}""", NullableContext.Default.NullableOutcome), "CodoMetis.TypeKit.Result`2[System.Int32,System.String]"),
            ("reading a result", () => JsonSerializer.Deserialize("""{"Outcome":{"State":1}}""", NullableContext.Default.NullableOutcome), "CodoMetis.TypeKit.Result`2[System.Int32,System.String]"),
        };

        foreach (var (@case, attempt, type) in attempts)
        {
            var exception = Should.Throw<InvalidOperationException>(attempt, @case);
            exception.Message.ShouldBe($"The converter '' is not compatible with the type '{type}'.", @case);
        }
    }

    private static void ShouldRefuse(Action attempt, string typeName, string advice, string @case)
    {
        var exception = Should.Throw<NotSupportedException>(attempt, @case);

        exception.Message.ShouldStartWith($"{typeName} ", Case.Sensitive, @case);
        exception.Message.ShouldContain(advice, Case.Sensitive, @case);
        exception.Message.ShouldNotContain(Secret, Case.Sensitive, @case);
    }

    /// <summary>
    /// Pins the one gap, so the README can state it: the serializer calls no converter for a property
    /// that is absent from the document, and the property is left <c>default</c>.
    /// </summary>
    [Fact]
    public void An_absent_property_reaches_no_converter_and_is_left_default()
    {
        var shape = JsonSerializer.Deserialize<Shape>("{}").ShouldNotBeNull();

        shape.Nickname.IsNone().ShouldBeTrue();
        shape.Outcome.State.ShouldBe(ResultState.Uninitialized);
    }

    [Fact]
    public void Requiring_the_constructor_parameters_makes_absence_an_error()
    {
        var options = new JsonSerializerOptions { RespectRequiredConstructorParameters = true };

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Shape>("{}", options));
    }

    [Fact]
    public void A_converter_registered_on_the_options_takes_precedence()
    {
        var options = new JsonSerializerOptions { Converters = { new NullForNoneFactory() } };

        JsonSerializer.Serialize(new Nicknamed(Option.Some("Ric")), options).ShouldBe("""{"Nickname":"Ric"}""");
        JsonSerializer.Serialize(new Nicknamed(Option.None<string>()), options).ShouldBe("""{"Nickname":null}""");
        JsonSerializer.Deserialize<Nicknamed>("""{"Nickname":null}""", options).ShouldNotBeNull().Nickname.IsNone().ShouldBeTrue();
        JsonSerializer.Deserialize<Nicknamed>("""{"Nickname":"Ric"}""", options).ShouldNotBeNull().Nickname.Or("").ShouldBe("Ric");
    }

    /// <summary>A dictionary key too, with the same message, rather than the serializer's about <see cref="object"/> keys.</summary>
    [Fact]
    public void A_dictionary_keyed_by_an_option_is_refused_with_the_same_message()
    {
        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(new Dictionary<Option<string>, int> { [Option.Some(Secret)] = 1 }))
              .Message.ShouldStartWith("Option<String> is not a wire type");
        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<Dictionary<Option<string>, int>>("""{"a":1}"""))
              .Message.ShouldStartWith("Option<String> is not a wire type");
    }

    [Fact]
    public void A_source_generated_context_honours_the_refusal()
    {
        var shape = new Shape(Option.Some(Secret), Result<int, string>.Success(1));

        Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(shape, Context.Default.Shape))
              .Message.ShouldNotContain(Secret);
    }

    [Fact]
    public void A_source_generated_context_still_takes_a_converter_from_the_options()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = Context.Default, Converters = { new NullForNoneFactory() } };

        JsonSerializer.Serialize(new Nicknamed(Option.Some("Ric")), options).ShouldBe("""{"Nickname":"Ric"}""");
    }

    /// <summary>
    /// Every exported struct of the base assembly refuses serialization. A struct with private state
    /// and no converter would write <c>{}</c>, so a new one fails here until it carries the converter
    /// or is exempted on purpose.
    /// </summary>
    [Fact]
    public void Every_exported_struct_of_the_assembly_carries_the_refusing_converter()
    {
        var structs = typeof(Option<>).Assembly.GetExportedTypes()
                                      .Where(type => type is { IsValueType: true, IsEnum: false })
                                      .Select(Close)
                                      .ToList();

        structs.Count.ShouldBeGreaterThanOrEqualTo(6, "Option, Result<TError>, Result<T, TError>, Success, Success<T>, Error<T>");

        foreach (var type in structs)
        {
            type.GetCustomAttribute<JsonConverterAttribute>()
                .ShouldNotBeNull($"{type} has no [JsonConverter]")
                .ConverterType.ShouldBe(typeof(NotWireTypeJsonConverterFactory), type.ToString());

            var instance = Activator.CreateInstance(type).ShouldNotBeNull();
            Should.Throw<NotSupportedException>(() => JsonSerializer.Serialize(instance, type), type.ToString());
            Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize("{}", type), type.ToString());
        }
    }

    private static Type Close(Type type) =>
        type.IsGenericTypeDefinition
            ? type.MakeGenericType([.. type.GetGenericArguments().Select(_ => typeof(string))])
            : type;

    private sealed record Shape(Option<string> Nickname, Result<int, string> Outcome);

    private sealed record Nicknamed(Option<string> Nickname);

    private sealed record NullableNickname(Option<string>? Nickname);

    private sealed record NullableOutcome(Result<int, string>? Outcome);

    /// <summary>Apart from <see cref="Context"/>, whose shapes a failed contract here must not reach.</summary>
    [JsonSerializable(typeof(NullableNickname))]
    [JsonSerializable(typeof(NullableOutcome))]
    private sealed partial class NullableContext : JsonSerializerContext;

    [JsonSerializable(typeof(Shape))]
    [JsonSerializable(typeof(Nicknamed))]
    [JsonSerializable(typeof(string))]
    private sealed partial class Context : JsonSerializerContext;

    /// <summary>The escape hatch an application might write: <c>Some(x)</c> as <c>x</c>, <c>None</c> as <c>null</c>.</summary>
    private sealed class NullForNoneFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(NullForNone<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

        private sealed class NullForNone<T> : JsonConverter<Option<T>> where T : notnull
        {
            public override bool HandleNull => true;

            public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType == JsonTokenType.Null
                    ? Option.None<T>()
                    : Option.Some(JsonSerializer.Deserialize<T>(ref reader, options)!);

            public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
            {
                if (value.TryGetValue(out var content)) JsonSerializer.Serialize(writer, content, options);
                else writer.WriteNullValue();
            }
        }
    }
}
