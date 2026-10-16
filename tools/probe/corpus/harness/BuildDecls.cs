// The hand half of C09Shapes.g.cs and C11Shapes.g.cs (generated from the rows, one value object per row): the tables' rows, and the
// wrapped types of C11's parse shapes.
namespace Probes.Build;

/// <summary>One C09 row as declared: the effective settings, the value object, its bases from the direct one up, and its factory.</summary>
public sealed record C09Case(int Index, string Key, string Kind, string Site, string Inheritance, Type Vo, Type[] Bases, Func<object, object> Make, (string Axis, string Value)[] Substituted);

/// <summary>
/// One C11 row as declared: the effective settings, the value object and the type it wraps, and for a parse shape its control: a value
/// object of the same kind, site and inheritance over the nearest type that does parse.
/// </summary>
public sealed record C11Case(int Index, string Key, string Kind, string Site, string Inheritance, Type Vo, Type Wrapped, Type? Control, Type? ControlWrapped, (string Axis, string Value)[] Substituted);

public static class Decls
{
    /// <summary>
    /// The row's settings as the generator keyed them; a table generated from other rows is a harness error, not a pass. Read from
    /// the cached copy of the row, so the run's own row marks no axis read.
    /// </summary>
    public static string Key(string probe, int index) =>
        string.Join(";", Probes.Shapes.Rows.Get(probe, index).All.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
}

// C11's parse shapes, each the whole wrapped type (CHANGELOG 2.1.0, Fixed): "A wrapped type's `Parse` whose result can't be assigned
// to it, or that takes its text by `ref`, or an abstract class's string constructor".

/// <summary>implicit-result: a static Parse whose result, an int, cannot be assigned to the type.</summary>
public sealed class C11ParsesToInt
{
    public static int Parse(string text) => text.Length;
    public override string ToString() => "parses-to-int";
}

/// <summary>ref-text: a static Parse that takes its text by ref.</summary>
public sealed class C11ParsesByRef
{
    public static C11ParsesByRef Parse(ref string text) { text = ""; return new(); }
    public override string ToString() => "parses-by-ref";
}

/// <summary>ref-text: a string constructor that takes its text by ref.</summary>
public sealed class C11ConstructedByRef
{
    public C11ConstructedByRef(ref string text) => text = "";
    private C11ConstructedByRef() { }
    public static C11ConstructedByRef Sample() => new();
    public override string ToString() => "constructed-by-ref";
}

/// <summary>abstract-string-ctor: an abstract class with a public string constructor, which only a derived class can call.</summary>
public abstract class C11AbstractText
{
    public C11AbstractText(string text) => Text = text;
    public string Text { get; }
    public override string ToString() => Text;
}

public sealed class C11ConcreteText(string text) : C11AbstractText(text);

// The controls, each the nearest type that can be parsed (Generators README, "What you can wrap": "Parsing where it can be parsed"):
// a Parse whose result converts to the type by a user-defined implicit operator, so it can be assigned; a Parse taking its text by in,
// which a call passes as it is; a sealed class's public string constructor.

/// <summary>Control of implicit-result: Parse gives a type that converts to this one implicitly.</summary>
public sealed class C11ParsedShape
{
    public string Text { get; init; } = "";
    public static C11ParsedCircle Parse(string text) => new() { Text = text };
    public override string ToString() => Text;
}

public sealed class C11ParsedCircle
{
    public string Text { get; init; } = "";
    public static implicit operator C11ParsedShape(C11ParsedCircle circle) => new() { Text = circle.Text };
}

/// <summary>Control of ref-text: Parse takes its text by in.</summary>
public sealed class C11ParsesFromIn
{
    public string Text { get; private init; } = "";
    public static C11ParsesFromIn Parse(in string text) => new() { Text = text };
    public override string ToString() => Text;
}

/// <summary>Control of abstract-string-ctor: a sealed class with a public string constructor.</summary>
public sealed class C11ConstructedConcrete(string text)
{
    public string Text { get; } = text;
    public override string ToString() => Text;
}
