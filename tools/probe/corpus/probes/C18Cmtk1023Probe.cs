// Claim C18-cmtk1023 (src/CodoMetis.TypeKit.Generators/README.md#build-errors): "CMTK1023 | A warning, and the value object is
// generated. A base record declares `Equals(TBase)`, `GetHashCode()` or `IEquatable<TBase>.Equals`, which the generated equality, the
// underlying value's, doesn't call." With README#equality: "a hand-written `Equals(TBase)`, `GetHashCode()` or `IEquatable<TBase>.Equals`
// on a base record isn't used, and a warning names it (CMTK1023)".
// Oracle: claim. One consumer build (Probes.Build.Consumer) holds every row's declaration in a file of its own: a value object of
// shape.kind over shape.wrapped at shape.site, over a chain of base records (shape.inheritance: the base-member on the topmost of 1, 2 or 3
// bases, or on the direct base beside an explicit IEquatable of its own or an explicit member of another interface), used at
// shape.position of a shape.containing type. Each file reports CMTK1023 once, as a warning, for that value object, naming each equality
// member its bases declare and nothing else; it reports no error; and the value object is generated (its From or TryFrom is in the
// built assembly).
// In this process too, the half of the sentence a run can show: over a base that declares the base-member by reference, the generated
// equality is the underlying value's (one value object per base-member, built by this project's own weaving).
using Probes.Build;
namespace Probes;

#pragma warning disable CMTK1023, CS8851 // the bases' own equality is the point; CS8851: the Equals base has no GetHashCode, on purpose
public enum C18TwinFault { No }
public abstract record C18EqualsBase { public virtual bool Equals(C18EqualsBase? other) => ReferenceEquals(this, other); }
public abstract record C18HashBase { public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
public abstract record C18EquatableBase : IEquatable<C18EquatableBase> { bool IEquatable<C18EquatableBase>.Equals(C18EquatableBase? other) => ReferenceEquals(this, other); }
public sealed partial record C18EqualsTwin : C18EqualsBase, CodoMetis.TypeKit.ValueObjects.IValidatedValue<C18EqualsTwin, int, C18TwinFault>
{ public static CodoMetis.TypeKit.Result<C18EqualsTwin, C18TwinFault> Create(int value) => new C18EqualsTwin(value); }
public sealed partial record C18HashTwin : C18HashBase, CodoMetis.TypeKit.ValueObjects.IValidatedValue<C18HashTwin, int, C18TwinFault>
{ public static CodoMetis.TypeKit.Result<C18HashTwin, C18TwinFault> Create(int value) => new C18HashTwin(value); }
public sealed partial record C18EquatableTwin : C18EquatableBase, CodoMetis.TypeKit.ValueObjects.IValidatedValue<C18EquatableTwin, int, C18TwinFault>
{ public static CodoMetis.TypeKit.Result<C18EquatableTwin, C18TwinFault> Create(int value) => new C18EquatableTwin(value); }
#pragma warning restore CMTK1023, CS8851

public sealed class C18Cmtk1023Probe
{
    const string Claim = "C18-cmtk1023";

    public static TheoryData<string, int> Data() => Probes.Shapes.Rows.Data(Claim);

    [Theory, MemberData(nameof(Data))]
    public Task Probe(string claim, int idx) => Probes.Shapes.Rec.Run(claim, idx, Check);

    /// <summary>A row as it is declared: the excluded values it carries replaced by the nearest that exists, each named.</summary>
    sealed record Shape(string Kind, string Site, string Inheritance, string Wrapped, string Member, string Containing, string Position, List<(string Axis, string Value, string Why)> Substituted);

    static Shape Of(Probes.Shapes.Row r)
    {
        var subs = new List<(string, string, string)>();
        var kind = r["shape.kind"];
        if (kind is not ("sealed-record-class" or "validated-class")) { subs.Add(("shape.kind", "sealed-record-class", "a record struct or a closed set has no base record (excluded)")); kind = "sealed-record-class"; }
        var site = r["shape.site"];
        if (site == "in-generic-type") { subs.Add(("shape.site", "nested-in-class", "a value object in a generic type is CMTK1005 (excluded)")); site = "nested-in-class"; }
        var inheritance = r["shape.inheritance"];
        if (inheritance == "none") { subs.Add(("shape.inheritance", "base-depth-1", "with no base there is no base member (excluded)")); inheritance = "base-depth-1"; }
        return new(kind, site, inheritance, r["shape.wrapped"], r["base-member"], r["shape.containing"], r["shape.position"], subs);
    }

    static int Depth(string inheritance) => inheritance switch { "base-depth-2" => 2, "base-depth-3" => 3, _ => 1 };

    /// <summary>The members CMTK1023 must name, as fragments of its message: the base-member, and the direct base's explicit IEquatable.</summary>
    static List<string> Named(Shape s, string id)
    {
        var q = Nesting(s, id);
        var top = $"{q}B{id}_{Depth(s.Inheritance)}";
        var named = new List<string>
        {
            s.Member switch
            {
                "Equals" => $"{top}.Equals({top}?)",
                "GetHashCode" => $"{top}.GetHashCode()",
                "IEquatable" => $"IEquatable<{top}>.Equals({top}?)",
                _ => throw new ArgumentException(s.Member),
            },
        };
        if (s.Inheritance == "base-explicit-IEquatable" && s.Member != "IEquatable") named.Add($"IEquatable<{q}B{id}_1>.Equals({q}B{id}_1?)");
        return named;
    }

    static string Source(Shape s, string id)
    {
        var depth = Depth(s.Inheritance);
        var body = new System.Text.StringBuilder();
        if (s.Inheritance == "explicit-interface-member") body.AppendLine($"public interface IMark{id} {{ int Mark(); }}");
        for (var d = depth; d >= 1; d--)
        {
            var name = $"B{id}_{d}";
            var interfaces = new List<string>();
            var members = new List<string>();
            if (d == depth)
                switch (s.Member)
                {
                    case "Equals": members.Add($"public virtual bool Equals({name}? other) => ReferenceEquals(this, other);"); break;
                    case "GetHashCode": members.Add("public override int GetHashCode() => 0;"); break;
                    case "IEquatable": interfaces.Add($"IEquatable<{name}>"); members.Add($"bool IEquatable<{name}>.Equals({name}? other) => ReferenceEquals(this, other);"); break;
                }
            if (d == 1 && s.Inheritance == "base-explicit-IEquatable" && s.Member != "IEquatable")
            { interfaces.Add($"IEquatable<{name}>"); members.Add($"bool IEquatable<{name}>.Equals({name}? other) => ReferenceEquals(this, other);"); }
            if (d == 1 && s.Inheritance == "explicit-interface-member") { interfaces.Add($"IMark{id}"); members.Add($"int IMark{id}.Mark() => 0;"); }
            var parents = (d == depth ? new List<string>() : [$"B{id}_{d + 1}"]).Concat(interfaces).ToList();
            body.AppendLine($"public abstract record {name}{(parents.Count > 0 ? " : " + string.Join(", ", parents) : "")} {{ {string.Join(" ", members)} }}");
        }
        var w = Src.Wrapped(s.Wrapped, id);
        body.AppendLine(s.Kind == "validated-class"
            ? $"public enum VF{id} {{ No }}\npublic sealed partial record V{id} : B{id}_1, IValidatedValue<V{id}, {w}, VF{id}>\n{{\n    public static Result<V{id}, VF{id}> Create({w} value) => new V{id}(value);\n}}"
            : $"public sealed partial record V{id} : B{id}_1, IValue<{w}>;");
        body.AppendLine(Container(s.Containing, s.Position, $"C{id}", $"V{id}"));
        return Src.Usings + $"namespace C18R{id};\n" + Src.WrappedDeclarations(id) + Src.AtSite(s.Site, id, body.ToString()) + "\n";
    }

    /// <summary>shape.containing using the value object at shape.position; no initializers, so a struct needs no constructor.</summary>
    static string Container(string containing, string position, string name, string v)
    {
        if (position == "record-ctor-parameter")
        {
            var record = containing is "record-class" or "record-struct";
            return Src.ContainerHead(containing, name, record ? $"({v} K)" : $"({v} k)") + (record ? ";" : $" {{ public {v} K => k; }}");
        }
        var member = position switch
        {
            "root" => $"public static {v}? Root() => null;",
            "property" => $"public {v}? P {{ get; set; }}",
            "nullable-property" => $"public {v}? N {{ get; set; }}",
            "field" => $"public {v}? F;",
            "list-element" => $"public List<{v}>? L {{ get; set; }}",
            "array-element" => $"public {v}[]? A {{ get; set; }}",
            "immutable-array-element" => $"public ImmutableArray<{v}> I {{ get; set; }}",
            "read-only-list-element" => $"public IReadOnlyList<{v}>? R {{ get; set; }}",
            "dictionary-key" => $"public Dictionary<{v}, int>? D {{ get; set; }}",
            "dictionary-value" => $"public Dictionary<int, {v}>? D {{ get; set; }}",
            "required-init" => $"public required {v} K {{ get; init; }}",
            "in-patch" => $"public Patch<{v}> Pa {{ get; set; }}",
            "polymorphic-derived-property" => $"public abstract class PB {{ }}\n    public sealed class PD : PB {{ public {v}? K {{ get; set; }} }}",
            _ => throw new ArgumentException("shape.position " + position),
        };
        return $"{Src.ContainerHead(containing, name)}\n{{\n    {member}\n}}";
    }

    /// <summary>The site's nesting as the diagnostic writes a type: its containing types, dotted.</summary>
    static string Nesting(Shape s, string id) => s.Site switch
    {
        "top-level" => "", "nested-in-class" or "nested-in-struct" => $"O{id}.", "nested-two-levels" => $"O{id}.P{id}.", _ => throw new ArgumentException(s.Site),
    };

    /// <summary>The value object's metadata name: the row's namespace, then the site's nesting.</summary>
    static string MetadataName(Shape s, string id) => $"C18R{id}." + s.Site switch
    {
        "top-level" => "", "nested-in-class" or "nested-in-struct" => $"O{id}+", "nested-two-levels" => $"O{id}+P{id}+", _ => throw new ArgumentException(s.Site),
    } + $"V{id}";

    /// <summary>Two value objects holding 42 over a base declaring the member by reference: equal, of one hash, and one holding 43 not.</summary>
    static string? Twin(string member)
    {
        (object A, object B, object C, object BaseA, object BaseB) v = member switch
        {
            "Equals" => (C18EqualsTwin.FromKnownGood(42), C18EqualsTwin.FromKnownGood(42), C18EqualsTwin.FromKnownGood(43), (C18EqualsBase)C18EqualsTwin.FromKnownGood(42), (C18EqualsBase)C18EqualsTwin.FromKnownGood(42)),
            "GetHashCode" => (C18HashTwin.FromKnownGood(42), C18HashTwin.FromKnownGood(42), C18HashTwin.FromKnownGood(43), C18HashTwin.FromKnownGood(42), C18HashTwin.FromKnownGood(42)),
            "IEquatable" => (C18EquatableTwin.FromKnownGood(42), C18EquatableTwin.FromKnownGood(42), C18EquatableTwin.FromKnownGood(43), C18EquatableTwin.FromKnownGood(42), C18EquatableTwin.FromKnownGood(42)),
            _ => throw new ArgumentException(member),
        };
        if (!v.A.Equals(v.B) || v.A.GetHashCode() != v.B.GetHashCode()) return $"over a base declaring {member} by reference, two value objects holding 42 are unequal or hash apart: the generated equality called the base's";
        if (v.A.Equals(v.C)) return $"over a base declaring {member}, value objects holding 42 and 43 are equal";
        if (member == "IEquatable" && !((IEquatable<C18EquatableBase>)v.BaseA).Equals((C18EquatableBase)v.BaseB)) return "the base's explicit IEquatable<TBase> compares two value objects holding 42 by reference";
        return null;
    }

    static Task<ConsumerOutcome> Built() => Consumer.Build(Claim, () =>
        Probes.Shapes.Rows.Load(Claim).ToDictionary(row => $"R{row.Index}.cs", row => Source(Of(row), row.Index.ToString())));

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var s = Of(r);
        foreach (var (axis, value, why) in s.Substituted) r.Substituted(axis, value, why);
        var id = r.Index.ToString();
        var build = await Built();
        var file = $"R{id}.cs";
        var mine = build.In(file).ToList();
        var errors = mine.Where(d => d.Severity == "error").ToList();
        if (errors.Any(e => e.Id.StartsWith("CS")))
        { r.Outcome("harness-error", "the probe's declaration does not compile: " + string.Join("; ", errors.Select(e => $"{e.Id} {e.Message}"))); return null; }
        if (errors.Count > 0) return "the value object is not generated: " + string.Join("; ", errors.Select(e => $"{e.Id} {e.Message}"));
        var reported = mine.Where(d => d.Id == "CMTK1023").ToList();
        if (reported.Count != 1) return $"CMTK1023 reported {reported.Count} times in {file}, expected once";
        var d = reported[0];
        if (d.Severity != "warning") return $"CMTK1023 is reported as {d.Severity}, documented a warning";
        if (!d.Message.StartsWith($"'{Nesting(s, id)}V{id}' inherits ", StringComparison.Ordinal))
            return "CMTK1023 does not name the value object: " + d.Message;
        var named = Named(s, id);
        var missing = named.Where(n => !d.Message.Contains(n, StringComparison.Ordinal)).ToList();
        if (missing.Count > 0) return $"CMTK1023 does not name {string.Join(", ", missing)}: {d.Message}";
        var count = System.Text.RegularExpressions.Regex.Matches(d.Message, " in its base type ").Count;
        if (count != named.Count) return $"CMTK1023 names {count} members, the bases declare {named.Count} ({string.Join(", ", named)}): {d.Message}";
        if (d.Message.Contains("Mark", StringComparison.Ordinal)) return "CMTK1023 names the explicit member of another interface: " + d.Message;
        if (build.Methods.Count == 0)
        { r.Outcome("harness-error", $"the consumer built no assembly (exit {build.ExitCode}): " + string.Join("; ", build.Diagnostics.Where(x => x.Severity == "error").Take(3).Select(x => $"{Path.GetFileName(x.File)} {x.Id} {x.Message}"))); return null; }
        var type = MetadataName(s, id);
        if (!build.Methods.TryGetValue(type, out var methods)) { r.Outcome("harness-error", $"no type {type} in the consumer"); return null; }
        var entry = s.Kind == "validated-class" ? "TryFrom" : "From";
        if (!methods.Contains(entry)) return $"the value object is not generated: {type} has no {entry} ({string.Join(", ", methods.Order())})";
        if (Twin(s.Member) is { } twin) return twin;
        r.Note("compared", $"{named.Count} members named");
        return null;
    }
}
