using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace CodoMetis.TypeKit.Benchmarks;

public sealed record RawLine(Guid Id, decimal Amount, string Code);

public sealed record Line(OrderId Id, Amount Amount, Code Code);

/// <summary>
/// Text and JSON. The raw JSON baseline does not validate <c>Code</c>, which the value object's
/// converter does through <c>Create</c>: the difference includes the rule.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class TextBenchmarks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly Guid    _guid    = Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee");
    private readonly OrderId _orderId = OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));

    private readonly char[] _buffer = new char[64];

    private string  _text    = null!;
    private RawLine _rawLine = null!;
    private Line    _line    = null!;
    private string  _rawJson = null!;
    private string  _json    = null!;

    [GlobalSetup]
    public void Setup()
    {
        _text    = _guid.ToString();
        _rawLine = new RawLine(_guid, 12.5m, "ABC");
        _line    = new Line(_orderId, Amount.From(12.5m), Code.FromKnownGood("ABC"));
        _rawJson = JsonSerializer.Serialize(_rawLine, Options);
        _json    = JsonSerializer.Serialize(_line, Options);

        if (_json != _rawJson) throw new InvalidOperationException($"The two shapes differ on the wire: {_json} vs {_rawJson}");
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Parse")]
    public Guid Guid_parse() => Guid.Parse(_text);

    [Benchmark, BenchmarkCategory("Parse")]
    public OrderId OrderId_parse() => OrderId.Parse(_text, null);

    [Benchmark(Baseline = true), BenchmarkCategory("Format")]
    public int Guid_format() => Format(_guid);

    [Benchmark, BenchmarkCategory("Format")]
    public int OrderId_format() => _orderId.TryFormat(_buffer, out var written, default, null) ? written : -1;

    // The same entry point for both: ISpanFormattable's, with a format and a provider, which is the
    // one a value object has and the one interpolation and string.Format call. Guid's own
    // parameterless-format overload skips it.
    private int Format<T>(T value) where T : ISpanFormattable => value.TryFormat(_buffer, out var written, default, null) ? written : -1;

    [Benchmark(Baseline = true), BenchmarkCategory("JsonWrite")]
    public string Raw_serialize() => JsonSerializer.Serialize(_rawLine, Options);

    [Benchmark, BenchmarkCategory("JsonWrite")]
    public string ValueObject_serialize() => JsonSerializer.Serialize(_line, Options);

    [Benchmark(Baseline = true), BenchmarkCategory("JsonRead")]
    public RawLine? Raw_deserialize() => JsonSerializer.Deserialize<RawLine>(_rawJson, Options);

    [Benchmark, BenchmarkCategory("JsonRead")]
    public Line? ValueObject_deserialize() => JsonSerializer.Deserialize<Line>(_json, Options);
}
