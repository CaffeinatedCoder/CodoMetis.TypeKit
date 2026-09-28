using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace CodoMetis.TypeKit.Benchmarks;

/// <summary>
/// A validated value object and a short result pipeline against the same logic written with a
/// nullable fault and branches.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ResultBenchmarks
{
    private readonly string[] _inputs = ["ABC", "abc", "ABCD", "XYZ"];

    [Benchmark(Baseline = true), BenchmarkCategory("Validate")]
    public int Raw_validate()
    {
        var valid = 0;
        foreach (var input in _inputs) if (Rules.Check(input) is null) valid++;
        return valid;
    }

    [Benchmark, BenchmarkCategory("Validate")]
    public int Create_validate()
    {
        var valid = 0;
        foreach (var input in _inputs) if (Code.Create(input).TryGetValue(out _, out _)) valid++;
        return valid;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Pipeline")]
    public int Raw_pipeline()
    {
        var total = 0;

        foreach (var input in _inputs)
        {
            if (Rules.Check(input) is not null) continue;

            var length = input.Length;
            if (length > 10) continue;

            total += length * 2;
        }

        return total;
    }

    [Benchmark, BenchmarkCategory("Pipeline")]
    public int Result_pipeline()
    {
        var total = 0;

        foreach (var input in _inputs)
        {
            total += Code.Create(input)
                         .Map(code => code.Value.Length)
                         .Bind(length => length > 10 ? Result<int, CodeFault>.Error(CodeFault.Length) : Result<int, CodeFault>.Success(length))
                         .Match(length => length * 2, _ => 0);
        }

        return total;
    }
}
