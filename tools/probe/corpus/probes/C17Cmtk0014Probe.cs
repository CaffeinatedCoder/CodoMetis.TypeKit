// Claim C17-cmtk0014 (src/CodoMetis.TypeKit.Analyzers/README.md#cmtk0014): "A FaultKind or a ConstructorAccessibility that names
// no member: a cast, default(FaultKind), a zero, or a folded constant" is reported (Warning), and "Both are reported where the value
// is written". The documented cases: `[RequireParameterlessConstructor((ConstructorAccessibility)7)]` and a fault set's member
// `new(default)`; "a default that is a member is silent" (ConstructorAccessibility.Public is 0).
// Oracle: claim. Each row writes the value (value axis) in the written form (site axis) at every place its shape axes describe, each
// place marked: in the declaration of shape.kind over shape.wrapped at shape.site (a fault set's member for closed-set, a static member
// of a value object otherwise), in the topmost base of its shape.inheritance chain and in an explicit interface member, in a member of a
// shape.containing type at shape.position, and in a declaration of shape.refused-kind (which compiles here, with no generators). An
// attribute-argument row also writes the documented RequireParameterlessConstructor case. Every place of an undefined value is reported
// exactly once at the value, naming its enum; a valid one nowhere; nothing else is reported.
using Probes.Build;
using Probes.Shapes;
namespace Probes;

public sealed class C17Cmtk0014Probe
{
    public static TheoryData<string, int> Data() => Rows.Data("C17Cmtk0014Probe");

    [Theory, MemberData(nameof(Data))]
    public Task Probe(string probe, int idx) => Rec.Run(probe, idx, Check);

    sealed class Places(string value, string site)
    {
        int n;
        public readonly HashSet<string> Expected = new();
        public readonly List<string> Labels = new();
        public bool Undefined => value != "valid";

        string Label() { var l = "p" + n++; Labels.Add(l); if (Undefined) Expected.Add(l); return l; }

        string Value(bool targetTyped) => (site, value) switch
        {
            ("cast", "(FaultKind)9") => "(FaultKind)9",
            ("cast", "default") => "(FaultKind)0",
            ("cast", "valid") => "(FaultKind)2",
            ("default-literal", _) => targetTyped ? "default" : "default(FaultKind)",
            ("attribute-argument", "(FaultKind)9") => "(FaultKind)9",
            ("attribute-argument", "default") => "default",
            ("attribute-argument", "valid") => "FaultKind.Conflict",
            _ => throw new ArgumentException($"{site} {value}"),
        };

        /// <summary>The expression for a place (marked unless the value goes into the attribute), and the attribute to put on its member.</summary>
        public (string E, string Attr) Place(bool targetTyped = true)
        {
            var l = Label();
            return site == "attribute-argument"
                ? ("FaultKind.NotFound", $"[ProbeKind({{|#{l}:{Value(true)}|}})] ")
                : ($"{{|#{l}:{Value(targetTyped)}|}}", "");
        }

        /// <summary>RequireParameterlessConstructor's argument: (ConstructorAccessibility)9 is reported, default (Public) and a member are not.</summary>
        public string Accessibility()
        {
            var l = "ca" + n++; Labels.Add(l);
            if (value == "(FaultKind)9") Expected.Add(l);
            var v = value switch { "(FaultKind)9" => "(ConstructorAccessibility)9", "default" => "default", _ => "ConstructorAccessibility.Internal" };
            return $"{{|#{l}:{v}|}}";
        }
    }

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var value = r["value"]; var site = r["site"];
        if (site == "default-literal" && value != "default")
        {
            r.Substituted("site", "cast", "a default literal is the value 0, so it cannot spell this value (excluded pair)");
            site = "cast";
        }
        var kind = r["shape.kind"]; var inheritance = r["shape.inheritance"];
        if (Src.IsStructKind(kind) && inheritance is not ("none" or "explicit-interface-member"))
        {
            r.Substituted("shape.inheritance", "none", "a record struct has no base type (excluded pair)");
            inheritance = "none";
        }
        var p = new Places(value, site);
        var id = r.Index.ToString();
        var body = new System.Text.StringBuilder();

        // The declaration of shape.kind over shape.wrapped, with its base chain and explicit member.
        var depth = inheritance switch { "base-depth-1" or "base-explicit-IEquatable" => 1, "base-depth-2" => 2, "base-depth-3" => 3, _ => 0 };
        var record = kind != "closed-set";
        for (var d = depth; d >= 1; d--)
        {
            var name = $"DB{id}_{d}"; var parent = d == depth ? "" : $" : DB{id}_{d + 1}";
            var equatable = d == 1 && inheritance == "base-explicit-IEquatable";
            if (equatable) parent = parent.Length == 0 ? $" : IEquatable<{name}>" : parent + $", IEquatable<{name}>";
            var members = d == depth ? Member(p.Place(), "BK") : "";
            if (equatable) members += $"\n    bool IEquatable<{name}>.Equals({name}? other) => ReferenceEquals(this, other);";
            body.AppendLine($"public abstract {(record ? "record" : "class")} {name}{parent}\n{{\n    {members}\n}}");
        }
        var explicitMember = inheritance == "explicit-interface-member";
        var mark = explicitMember ? $", IMark{id}" : "";
        if (explicitMember) body.AppendLine($"public interface IMark{id} {{ FaultKind Mark(); }}");
        var baseList = depth > 0 ? $"DB{id}_1, " : "";
        string Explicit() { if (!explicitMember) return ""; var (e, a) = p.Place(); return $"\n    {a}FaultKind IMark{id}.Mark() => {e};"; }
        if (kind == "closed-set")
        {
            var (e, a) = p.Place();
            body.AppendLine($$"""
                public sealed partial class D{{id}} : {{baseList}}IFaultSet<D{{id}}>{{mark}}
                {
                    public static readonly D{{id}} A = new(FaultKind.NotFound);
                    {{a}}public static readonly D{{id}} M = new({{e}});
                    private D{{id}}(FaultKind kind) => Kind = kind;
                    public FaultKind Kind { get; }
                    public string Name => "";
                    public string QualifiedName => "";
                    public static IReadOnlyList<D{{id}}> All => [A, M];
                    public static bool TryParse(string? name, out D{{id}} member) { member = A; return true; }{{Explicit()}}
                }
                """);
        }
        else
        {
            var w = Src.Wrapped(r["shape.wrapped"], id);
            var head = kind switch
            {
                "record-struct" => $"public readonly partial record struct D{id} : IValue<{w}>{mark}",
                "sealed-record-class" => $"public sealed partial record D{id} : {baseList}IValue<{w}>{mark}",
                "validated-struct" => $"public readonly partial record struct D{id} : IValidatedValue<D{id}, {w}, DF{id}>{mark}",
                "validated-class" => $"public sealed partial record D{id} : {baseList}IValidatedValue<D{id}, {w}, DF{id}>{mark}",
                _ => throw new ArgumentException(kind),
            };
            var create = kind.StartsWith("validated") ? $"\n    public static Result<D{id}, DF{id}> Create({w} value) => Result.Error(DF{id}.No);" : "";
            body.AppendLine($"public enum DF{id} {{ No }}");
            body.AppendLine($"{head}\n{{\n    {Member(p.Place(), "K")}{create}{Explicit()}\n}}");
        }

        // shape.refused-kind: refused by the generators, an ordinary declaration to the compiler.
        var refused = r["shape.refused-kind"];
        var rm = Member(p.Place(), "RK");
        body.AppendLine(refused switch
        {
            "unsealed-record-class (CMTK1006)" => $"public partial record R{id} : IValue<string> {{ {rm} }}",
            "over-value-object (CMTK1005)" => $"public readonly partial record struct RIn{id} : IValue<int>;\npublic sealed partial record R{id} : IValue<RIn{id}> {{ {rm} }}",
            "generic-wrapped (CMTK1005)" => $"public readonly partial record struct R{id} : IValue<List<int>> {{ {rm} }}",
            "abstract (CMTK1005)" => $"public abstract partial record R{id} : IValue<string> {{ {rm} }}",
            _ => throw new ArgumentException(refused),
        });

        // shape.containing at shape.position.
        body.AppendLine(Container(r["shape.containing"], r["shape.position"], $"C{id}", p));

        if (site == "attribute-argument")
            body.AppendLine($"[RequireParameterlessConstructor({p.Accessibility()})] public interface IPlug{id} {{ }}");

        var source = Src.Usings + Src.WrappedDeclarations(id) +
            "[AttributeUsage(AttributeTargets.All, AllowMultiple = true)] public sealed class ProbeKindAttribute(FaultKind kind) : Attribute { public FaultKind Kind { get; } = kind; }\n" +
            Src.AtSite(r["shape.site"], id, body.ToString());
        var run = await Rz.Analyze("CMTK0014", [("Row" + id + ".cs", source)]);
        if (run.Errors.Count > 0) { r.Outcome("harness-error", "the probe's source does not compile: " + run.ErrorText()); return null; }
        if (run.AnalyzerFailures.Count > 0) return "the analyzer failed: " + string.Join("; ", run.AnalyzerFailures.Select(f => f.GetMessage()));
        r.Note(NoteKeys.Compared, $"{p.Labels.Count} places, {p.Expected.Count} expected reported");
        return Rz.Agree(run, p.Expected, (d, m) =>
        {
            var type = m.Label.StartsWith("ca") ? "ConstructorAccessibility" : "FaultKind";
            return d.GetMessage().Contains($"names no member of {type}") ? null : $"the report does not name {type}: {d.GetMessage()}";
        });
    }

    static string Member((string E, string Attr) place, string name) => $"{place.Attr}public static FaultKind {name} => {place.E};";

    /// <summary>shape.containing holding the value at shape.position; static members, so a struct needs no constructor.</summary>
    static string Container(string containing, string position, string name, Places p)
    {
        var self = Src.ContainerSelf(containing, name);
        if (position == "record-ctor-parameter")
        {
            var (e, a) = p.Place();
            var record = containing is "record-class" or "record-struct";
            var parameter = record ? $"({a}FaultKind K = {e})" : $"({a}FaultKind k = {e})";
            return Src.ContainerHead(containing, name, parameter) + (record ? ";" : " { public FaultKind K => k; }");
        }
        string member;
        if (position == "required-init")
        {
            var (e, a) = p.Place();
            member = $"{a}public required FaultKind K {{ get; init; }}\n    public static {self} Make() => new() {{ K = {e} }};";
        }
        else if (position == "polymorphic-derived-property")
        {
            var (e, a) = p.Place();
            member = $"public abstract class PB {{ public abstract FaultKind K {{ get; }} }}\n    public sealed class PD : PB {{ {a}public override FaultKind K => {e}; }}";
        }
        else
        {
            var (e, a) = p.Place(targetTyped: position != "nullable-property");
            member = a + position switch
            {
                "root" => $"public static FaultKind Root() => {e};",
                "property" => $"public static FaultKind P {{ get; }} = {e};",
                "nullable-property" => $"public static FaultKind? N {{ get; }} = {e};",
                "field" => $"public static readonly FaultKind F = {e};",
                "list-element" => $"public static readonly List<FaultKind> L = [{e}];",
                "array-element" => $"public static readonly FaultKind[] A = [{e}];",
                "immutable-array-element" => $"public static readonly ImmutableArray<FaultKind> I = [{e}];",
                "read-only-list-element" => $"public static readonly IReadOnlyList<FaultKind> R = [{e}];",
                "dictionary-key" => $"public static readonly Dictionary<FaultKind, int> D = new() {{ [{e}] = 1 }};",
                "dictionary-value" => $"public static readonly Dictionary<int, FaultKind> D = new() {{ [1] = {e} }};",
                "in-patch" => $"public static Patch<FaultKind> Pa => Patch.Value<FaultKind>({e});",
                _ => throw new ArgumentException("shape.position " + position),
            };
        }
        return $"{Src.ContainerHead(containing, name)}\n{{\n    {member}\n}}";
    }
}
