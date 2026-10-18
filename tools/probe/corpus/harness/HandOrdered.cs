// Value objects that declare their own CompareTo(TSelf) (a descending order), for the EF claims about hand-written ordering.
#nullable enable
#pragma warning disable CMTK0014, CS8618, CS1591, CMTK0005, CMTK1000, CMTK0003, CMTK0006
using CodoMetis.TypeKit.ValueObjects;
namespace Probes.Ef;

public readonly partial record struct HO_int_struct : IValue<int> { public int CompareTo(HO_int_struct other) => other.Value.CompareTo(Value); }
public readonly partial record struct HO_string_struct : IValue<string> { public int CompareTo(HO_string_struct other) => string.CompareOrdinal(other.Value, Value); }
public readonly partial record struct HO_decimal_struct : IValue<decimal> { public int CompareTo(HO_decimal_struct other) => other.Value.CompareTo(Value); }
public readonly partial record struct HO_DateTime_struct : IValue<DateTime> { public int CompareTo(HO_DateTime_struct other) => other.Value.CompareTo(Value); }
public sealed partial record class HO_int_class : IValue<int> { public int CompareTo(HO_int_class? other) => other is null ? 1 : other.Value.CompareTo(Value); }
public sealed partial record class HO_string_class : IValue<string> { public int CompareTo(HO_string_class? other) => other is null ? 1 : string.CompareOrdinal(other.Value, Value); }

public static class HandOrdered
{
    public sealed record Entry(string Name, Type Vo, Type Plain, object[] Values);
    public static readonly Entry[] All =
    [
        new("int-struct", typeof(HO_int_struct), typeof(int), [1, 2, 3]),
        new("string-struct", typeof(HO_string_struct), typeof(string), ["a", "b", "c"]),
        new("decimal-struct", typeof(HO_decimal_struct), typeof(decimal), [1.5m, 2.5m, 3.5m]),
        new("DateTime-struct", typeof(HO_DateTime_struct), typeof(DateTime), [new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)]),
        new("int-class", typeof(HO_int_class), typeof(int), [1, 2, 3]),
        new("string-class", typeof(HO_string_class), typeof(string), ["a", "b", "c"]),
    ];
    public static Entry Of(string name) => All.First(e => e.Name == name);
    public static object Make(Entry e, object plain) => e.Vo.GetMethod("From", [e.Plain])!.Invoke(null, [plain])!;
}
