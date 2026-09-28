using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace CodoMetis.TypeKit.Benchmarks;

/// <summary>Equality, hashing and sorting: what a collection does with a value object all day.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class IdentityBenchmarks
{
    private const int Count = 1_000;

    private readonly Guid[]    _guids    = new Guid[Count];
    private readonly OrderId[] _orderIds = new OrderId[Count];

    private readonly decimal[] _decimals = new decimal[Count];
    private readonly Amount[]  _amounts  = new Amount[Count];

    private readonly DayOfWeek[] _days     = new DayOfWeek[Count];
    private readonly Weekday[]   _weekdays = new Weekday[Count];

    private readonly DayOfWeek[] _dayScratch     = new DayOfWeek[Count];
    private readonly Weekday[]   _weekdayScratch = new Weekday[Count];

    private readonly decimal[] _decimalScratch = new decimal[Count];
    private readonly Amount[]  _amountScratch  = new Amount[Count];

    private Dictionary<Guid, int>    _byGuid    = null!;
    private Dictionary<OrderId, int> _byOrderId = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);

        for (var i = 0; i < Count; i++)
        {
            _guids[i]    = Guid.CreateVersion7();
            _orderIds[i] = OrderId.From(_guids[i]);
            _decimals[i] = random.Next(0, 1_000_000) / 100m;
            _amounts[i]  = Amount.From(_decimals[i]);
            _days[i]     = (DayOfWeek)random.Next(0, 7);
            _weekdays[i] = Weekday.From(_days[i]);
        }

        _byGuid    = _guids.Select((g, i) => (g, i)).ToDictionary(x => x.g, x => x.i);
        _byOrderId = _orderIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Equality")]
    public int Guid_equality()
    {
        var matches = 0;
        for (var i = 1; i < Count; i++) if (_guids[i] == _guids[i - 1]) matches++;
        return matches;
    }

    [Benchmark, BenchmarkCategory("Equality")]
    public int OrderId_equality()
    {
        var matches = 0;
        for (var i = 1; i < Count; i++) if (_orderIds[i] == _orderIds[i - 1]) matches++;
        return matches;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Lookup")]
    public int Guid_dictionary_lookup()
    {
        var sum = 0;
        foreach (var guid in _guids) sum += _byGuid[guid];
        return sum;
    }

    [Benchmark, BenchmarkCategory("Lookup")]
    public int OrderId_dictionary_lookup()
    {
        var sum = 0;
        foreach (var id in _orderIds) sum += _byOrderId[id];
        return sum;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Sort")]
    public decimal Decimal_sort()
    {
        Array.Copy(_decimals, _decimalScratch, Count);
        Array.Sort(_decimalScratch);
        return _decimalScratch[0];
    }

    [Benchmark, BenchmarkCategory("Sort")]
    public Amount Amount_sort()
    {
        Array.Copy(_amounts, _amountScratch, Count);
        Array.Sort(_amountScratch);
        return _amountScratch[0];
    }

    [Benchmark(Baseline = true), BenchmarkCategory("EnumSort")]
    public DayOfWeek Enum_sort()
    {
        Array.Copy(_days, _dayScratch, Count);
        Array.Sort(_dayScratch);
        return _dayScratch[0];
    }

    [Benchmark, BenchmarkCategory("EnumSort")]
    public Weekday Weekday_sort()
    {
        Array.Copy(_weekdays, _weekdayScratch, Count);
        Array.Sort(_weekdayScratch);
        return _weekdayScratch[0];
    }
}
