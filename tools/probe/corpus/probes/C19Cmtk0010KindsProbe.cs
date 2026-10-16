// Claim C19-cmtk0010-kinds (docs/type-constraints.md#the-kind-of-a-type-argument, with its "Two parameters apart" and the list of
// what the rule checks): [Interface], [Unsealed], [ValueObject], [ClosedSet], [FlagsEnum], [ReadOnlyStruct], [Distinct] and [Unrelated]
// reject the kinds the page's table names, CMTK0010 (Error), "at the methods the compiler calls for you: a foreach's GetEnumerator, an
// await's GetAwaiter, a Deconstruct (in var (a, b) = x, a foreach, or a pattern like x is (1, 2)), and a collection expression's Add or
// builder method ... every other attribute is [checked]", and at a call; "An inferred one is reported at the call ... the foreach's
// collection, the await, the deconstruction, the pattern or the collection expression"; a pair's breach "once, at the later argument".
// Oracle: the page's table ("Accepts") over what each argument is. Each row compiles, with the CMTK0010 analyzer:
//  - the catalogue (test/CodoMetis.TypeKit.Analyzers.Tests/Variants: TypeArgumentSites' subjects and its uses of the row's site,
//    TypeArgumentKinds' candidates), with one argument that breaks the attribute and one that meets it (a pair attribute: subjects of
//    two parameters at the same sites), plus a collection expression's builder method, which the catalogue has no use of;
//  - the row's shapes as arguments at the same sites: the value object or closed set of shape.kind over shape.wrapped at shape.site,
//    its base and its explicit interface (shape.inheritance), the wrapped type itself, and a declaration of shape.refused-kind (one
//    that compiles without the generators; the two that do not are excluded); each use hosted in a member of shape.containing at
//    shape.position.
// Every breaking use is reported exactly once at its span, no meeting one anywhere.
using Probes.Build;
using NoteKeys = Probes.Shapes.NoteKeys;
using CodoMetis.TypeKit.Analyzers.Tests.Variants;
namespace Probes;

public sealed class C19Cmtk0010KindsProbe
{
    public static TheoryData<string, int> Data() => Probes.Shapes.Rows.Data("C19Cmtk0010KindsProbe");

    [Theory, MemberData(nameof(Data))]
    public Task Probe(string probe, int idx) => Probes.Shapes.Rec.Run(probe, idx, Check);

    /// <summary>What a type is, as the page's table reads it.</summary>
    sealed record Facts(bool Interface = false, bool Class = false, bool Sealed = false, bool Struct = false, bool ReadOnly = false, bool Enum = false, bool Flags = false, bool ValueObject = false, bool ClosedSet = false);

    /// <summary>The table's "Accepts" column.</summary>
    static bool Accepts(string attribute, Facts f) => attribute switch
    {
        "Interface" => f.Interface,                                   // an interface
        "Unsealed" => f.Class && !f.Sealed,                           // a class or record class that can be derived from, abstract included
        "ValueObject" => f.ValueObject && !f.Interface,               // a struct or class that implements IValue<T> for any T
        "ClosedSet" => f.ClosedSet,                                   // a type that implements IClosedSet<TSelf>
        "FlagsEnum" => f.Enum && f.Flags,                             // an enum marked [Flags]
        "ReadOnlyStruct" => f.Struct && f.ReadOnly && !f.Enum,        // a readonly struct or readonly record struct, the primitives included
        _ => throw new ArgumentException(attribute),
    };

    static Facts OfKind(string kind) => kind switch
    {
        "record-struct" or "validated-struct" => new(Struct: true, ReadOnly: true, Sealed: true, ValueObject: true),
        "sealed-record-class" or "validated-class" => new(Class: true, Sealed: true, ValueObject: true),
        "closed-set" => new(Class: true, Sealed: true, ClosedSet: true),
        _ => throw new ArgumentException(kind),
    };

    static Facts OfRefused(string refused) => refused switch
    {
        "unsealed-record-class (CMTK1006)" => new(Class: true, ValueObject: true),
        "over-value-object (CMTK1005)" => new(Class: true, Sealed: true, ValueObject: true),
        "generic-wrapped (CMTK1005)" => new(Struct: true, ReadOnly: true, Sealed: true, ValueObject: true),
        "abstract (CMTK1005)" => new(Class: true, ValueObject: true),
        "over-object (CMTK1005)" => new(Class: true, Sealed: true, ValueObject: true),
        "over-System.Enum (CMTK1005)" => new(Struct: true, ReadOnly: true, Sealed: true, ValueObject: true),
        "named-as-generated-member (CMTK1005)" => new(Struct: true, ReadOnly: true, Sealed: true, ValueObject: true),
        _ => throw new ArgumentException(refused),
    };

    static readonly Dictionary<string, Type> Runtime = new()
    {
        ["string"] = typeof(string), ["Guid"] = typeof(Guid), ["int"] = typeof(int), ["long"] = typeof(long), ["byte"] = typeof(byte), ["ulong"] = typeof(ulong),
        ["decimal"] = typeof(decimal), ["double"] = typeof(double), ["bool"] = typeof(bool), ["char"] = typeof(char), ["DateTime"] = typeof(DateTime),
        ["DateTimeOffset"] = typeof(DateTimeOffset), ["DateOnly"] = typeof(DateOnly), ["TimeOnly"] = typeof(TimeOnly), ["TimeSpan"] = typeof(TimeSpan),
        ["Uri"] = typeof(Uri), ["NodaTime.Instant"] = typeof(NodaTime.Instant),
    };

    /// <summary>The wrapped type: the ones declared in the row's source by what they are declared as, the framework's by reflection.</summary>
    static Facts OfWrapped(string wrapped)
    {
        switch (wrapped)
        {
            case "enum": return new(Enum: true, Struct: true, Sealed: true);
            case "flags-enum": return new(Enum: true, Flags: true, Struct: true, Sealed: true);
            case "own-record-class": return new(Class: true, Sealed: true);
        }
        var t = Runtime[wrapped];
        // A library for netstandard2.0 (NodaTime) carries its own IsReadOnlyAttribute, so it is read by its full name.
        var readOnly = t.IsValueType && t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        return new(Class: t.IsClass, Sealed: t.IsSealed, Struct: t.IsValueType, ReadOnly: readOnly || t.IsPrimitive || t == typeof(decimal));
    }

    static readonly string[] Pair = ["Distinct", "Unrelated"];

    /// <summary>The catalogue's uses of the claim's site value.</summary>
    static string[] CatalogueSites(string site) => site switch
    {
        "call" => ["a call with written arguments", "a call with inferred arguments"],
        "foreach" => ["a foreach's GetEnumerator"],
        "await" => ["an await's GetAwaiter"],
        "Deconstruct" => ["a deconstruction", "a positional pattern"],
        "collection-expression" => ["a collection expression's Add"],
        _ => throw new ArgumentException(site),
    };

    /// <summary>A builder for a collection expression, the "or builder" the page names; one parameter and two.</summary>
    const string Builders =
        """
        public static class ProbeBuilders
        {
            public static Built<T> Create<{C} T>(ReadOnlySpan<T> items) => new();
        }
        [System.Runtime.CompilerServices.CollectionBuilder(typeof(ProbeBuilders), nameof(ProbeBuilders.Create))]
        public sealed class Built<T> : IEnumerable<T>
        {
            public IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        """;

    /// <summary>The catalogue's sites with two constrained parameters, for [Distinct] and [Unrelated].</summary>
    const string PairSubjects =
        """
        public static class PairRunner { public static void Run<{C} T1, {C} T2>(T1 first, T2 second) { } }
        public sealed class Bag2<T1, T2> { }
        public sealed class Heap2<T1, T2> : IEnumerable<T1>
        {
            public IEnumerator<T1> GetEnumerator() => throw new NotSupportedException();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public static class PairPatterns
        {
            public static Cursor<T1> GetEnumerator<{C} T1, {C} T2>(this Bag2<T1, T2> bag) => default;
            public static Waiter<T1> GetAwaiter<{C} T1, {C} T2>(this Bag2<T1, T2> bag) => default;
            public static void Deconstruct<{C} T1, {C} T2>(this Bag2<T1, T2> bag, out int first, out int second) => (first, second) = (0, 0);
            public static void Add<{C} T1, {C} T2>(this Heap2<T1, T2> heap, T1 item) { }
        }
        public static class PairBuilders
        {
            public static Built2<T1, T2> Create<{C} T1, {C} T2>(ReadOnlySpan<T1> items) => new();
        }
        [System.Runtime.CompilerServices.CollectionBuilder(typeof(PairBuilders), nameof(PairBuilders.Create))]
        public sealed class Built2<T1, T2> : IEnumerable<T1>
        {
            public IEnumerator<T1> GetEnumerator() => throw new NotSupportedException();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public delegate System.Threading.Tasks.Task GoP<TF, TS>(TF x, TS y, Bag2<TF, TS> bag2);
        public delegate System.Threading.Tasks.Task GoD<TG>(TG a, Holder? holder, Bag<TG> bag, Crate<TG> crate, object o);

        """;

    /// <summary>The statements of a pair site: x and y of the two arguments, bag2 of both. {F} and {S} are the arguments.</summary>
    static string[] PairStatements(string site) => site switch
    {
        "call" => ["PairRunner.Run<{F}, {|#0:{S}|}>(x, y);", "{|#0:PairRunner.Run(x, y)|};"],
        "foreach" => ["foreach (var item in {|#0:bag2|}) { }"],
        "await" => ["_ = {|#0:await bag2|};"],
        "Deconstruct" => ["{|#0:var (p, q) = bag2|};", "_ = bag2 is {|#0:(1, 2)|};"],
        "collection-expression" => ["Heap2<{F}, {S}> heap = {|#0:[x]|};", "Built2<{F}, {S}> built = {|#0:[x]|};"],
        _ => throw new ArgumentException(site),
    };

    sealed class Uses
    {
        int n;
        public readonly HashSet<string> Expected = new();
        public readonly List<string> All = new();
        public readonly System.Text.StringBuilder Core = new();
        /// <summary>Relabels a statement's {|#0:…|} with a label of its own; its breaking or meeting is recorded.</summary>
        public string Label(string statements, bool breaking)
        {
            var l = "u" + n++; All.Add(l); if (breaking) Expected.Add(l);
            return statements.Replace("{|#0:", "{|#" + l + ":");
        }
        public int Next => n++;
    }

    static (string Prefix, string Suffix) BodyFrame()
    {
        var t = TypeArgumentSites.Body("\u0001");
        var at = t.IndexOf('\u0001');
        return (t[..at], t[(at + 1)..]);
    }

    static string StatementsOf(string siteName)
    {
        var use = TypeArgumentSites.All.Single(s => s.Name == siteName).Use;
        var (prefix, suffix) = BodyFrame();
        if (!use.StartsWith(prefix, StringComparison.Ordinal) || !use.EndsWith(suffix, StringComparison.Ordinal))
            throw new InvalidOperationException($"the catalogue's use '{siteName}' is not written through TypeArgumentSites.Body");
        return use[prefix.Length..^suffix.Length];
    }

    static async Task<string?> Check(Probes.Shapes.Row r)
    {
        var attribute = r["attribute"]; var site = r["site"];
        var pair = Pair.Contains(attribute);
        var constraint = $"[{attribute}]";
        var uses = new Uses();
        var id = r.Index.ToString();

        // The catalogue, with an argument that breaks the attribute and one that meets it.
        if (!pair)
        {
            var (breaking, meeting) = attribute switch
            {
                "Interface" => ("OrderHandler", "IHandler"),
                "Unsealed" => ("OrderHandler", "Handler"),
                "ValueObject" => ("Guid", "Id"),
                "ClosedSet" => ("Id", "Palette"),
                "FlagsEnum" => ("Shade", "Rights"),
                "ReadOnlyStruct" => ("Mutable", "Frozen"),
                _ => throw new ArgumentException(attribute),
            };
            var names = CatalogueSites(site);
            var k = 0;
            foreach (var name in names)
                foreach (var (arg, breaks) in new[] { (breaking, true), (meeting, false) })
                {
                    var use = TypeArgumentSites.All.Single(s => s.Name == name).Use.Replace("{A}", arg)
                        .Replace("public static class Use", $"public static class Use{k++}");
                    uses.Core.AppendLine(uses.Label(use, breaks));
                }
            if (site == "collection-expression")
                foreach (var (arg, breaks) in new[] { (breaking, true), (meeting, false) })
                    uses.Core.AppendLine(uses.Label(TypeArgumentSites.Body("Built<{A}> built = {|#0:[a]|};").Replace("{A}", arg).Replace("public static class Use", $"public static class Use{k++}"), breaks));
        }
        else
        {
            // [Unrelated] also keeps a type and its base apart; [Distinct] only one type twice.
            var cases = new List<(string F, string S, bool Breaks)> { ("int", "string", false), ("Placed", "Placed", true), ("Handler", "OrderHandler", attribute == "Unrelated") };
            var k = 0;
            foreach (var (f, s, breaks) in cases)
                foreach (var statement in PairStatements(site))
                    uses.Core.AppendLine(uses.Label(PairHost("root", "class", $"Pair{k++}", f, s, statement), breaks));
        }

        // The row's shapes.
        var kind = r["shape.kind"]; var inheritance = r["shape.inheritance"];
        if (Src.IsStructKind(kind) && inheritance is not ("none" or "explicit-interface-member"))
        {
            r.Substituted("shape.inheritance", "none", "a record struct has no base type (excluded pair)");
            inheritance = "none";
        }
        var depth = inheritance switch { "base-depth-1" or "base-explicit-IEquatable" => 1, "base-depth-2" => 2, "base-depth-3" => 3, _ => 0 };
        var decl = new System.Text.StringBuilder();
        var record = kind != "closed-set";
        for (var d = depth; d >= 1; d--)
        {
            var name = $"DB{id}_{d}"; var parent = d == depth ? "" : $" : DB{id}_{d + 1}";
            var equatable = d == 1 && inheritance == "base-explicit-IEquatable";
            if (equatable) parent = parent.Length == 0 ? $" : IEquatable<{name}>" : parent + $", IEquatable<{name}>";
            decl.AppendLine($"public abstract {(record ? "record" : "class")} {name}{parent} {{ {(equatable ? $"bool IEquatable<{name}>.Equals({name}? other) => ReferenceEquals(this, other);" : "")} }}");
        }
        var explicitMember = inheritance == "explicit-interface-member";
        var mark = explicitMember ? $", IMark{id}" : "";
        var markMember = explicitMember ? $" int IMark{id}.Mark() => 0;" : "";
        if (explicitMember) decl.AppendLine($"public interface IMark{id} {{ int Mark(); }}");
        var baseList = depth > 0 ? $"DB{id}_1, " : "";
        string? wrapped = null;
        if (kind == "closed-set")
            decl.AppendLine($$"""
                public sealed partial class D{{id}} : {{baseList}}IClosedSet<D{{id}}>{{mark}}
                {
                    public static readonly D{{id}} A = new();
                    public string Name => "";
                    public string QualifiedName => "";
                    public static IReadOnlyList<D{{id}}> All => [A];
                    public static bool TryParse(string? name, out D{{id}} member) { member = A; return true; }{{markMember}}
                }
                """);
        else
        {
            wrapped = r["shape.wrapped"];
            var w = Src.Wrapped(wrapped, id);
            decl.AppendLine($"public enum DF{id} {{ No }}");
            decl.AppendLine(kind switch
            {
                "record-struct" => $"public readonly partial record struct D{id} : IValue<{w}>{mark} {{{markMember} }}",
                "sealed-record-class" => $"public sealed partial record D{id} : {baseList}IValue<{w}>{mark} {{{markMember} }}",
                "validated-struct" => $"public readonly partial record struct D{id} : IValidatedValue<D{id}, {w}, DF{id}>{mark} {{ public static Result<D{id}, DF{id}> Create({w} value) => Result.Error(DF{id}.No);{markMember} }}",
                "validated-class" => $"public sealed partial record D{id} : {baseList}IValidatedValue<D{id}, {w}, DF{id}>{mark} {{ public static Result<D{id}, DF{id}> Create({w} value) => Result.Error(DF{id}.No);{markMember} }}",
                _ => throw new ArgumentException(kind),
            });
        }
        // shape.refused-kind: refused by the generators, an ordinary declaration to the compiler. The two kinds that are not (a closed
        // set named as a generated member, CS0535; a hand-written operator ==, CS0111) are excluded in claims.json, and a row the
        // covering array fills with one runs over object instead.
        var refused = r["shape.refused-kind"];
        if (refused is "closed-set-named-as-generated-member (CMTK1021)" or "equality-operator (CMTK1011)")
        {
            r.Substituted("shape.refused-kind", "over-object (CMTK1005)", "it does not compile without the generators (excluded pair)");
            refused = "over-object (CMTK1005)";
        }
        var rName = refused == "named-as-generated-member (CMTK1005)" ? Refusals.ValueObjectNames[r.Index % Refusals.ValueObjectNames.Length] : $"R{id}";
        decl.AppendLine(refused switch
        {
            "unsealed-record-class (CMTK1006)" => $"public partial record R{id} : IValue<string>;",
            "over-value-object (CMTK1005)" => $"public readonly partial record struct RIn{id} : IValue<int>;\npublic sealed partial record R{id} : IValue<RIn{id}>;",
            "generic-wrapped (CMTK1005)" => $"public readonly partial record struct R{id} : IValue<List<int>>;",
            "abstract (CMTK1005)" => $"public abstract partial record R{id} : IValue<string>;",
            "over-object (CMTK1005)" => $"public sealed partial record R{id} : IValue<object>;",
            "over-System.Enum (CMTK1005)" => $"public readonly partial record struct R{id} : IValue<System.Enum>;",
            "named-as-generated-member (CMTK1005)" => $"public readonly partial record struct {rName} : IValue<int>;",
            _ => throw new ArgumentException(refused),
        });

        var containing = r["shape.containing"]; var position = r["shape.position"];
        var dFacts = OfKind(kind);
        if (!pair)
        {
            var arguments = new List<(string Type, bool Breaks)> { ($"D{id}", !Accepts(attribute, dFacts)), (rName, !Accepts(attribute, OfRefused(refused))) };
            if (depth > 0) arguments.Add(($"DB{id}_{depth}", !Accepts(attribute, new Facts(Class: true))));
            if (explicitMember) arguments.Add(($"IMark{id}", !Accepts(attribute, new Facts(Interface: true))));
            if (wrapped is not null) arguments.Add((Src.Wrapped(wrapped, id), !Accepts(attribute, OfWrapped(wrapped))));
            foreach (var (type, breaks) in arguments)
                foreach (var name in CatalogueSites(site))
                    decl.AppendLine(uses.Label(Host(position, containing, $"C{id}_{uses.Next}", type, StatementsOf(name)), breaks));
            if (site == "collection-expression")
                foreach (var (type, breaks) in arguments)
                    decl.AppendLine(uses.Label(Host(position, containing, $"C{id}_{uses.Next}", type, "Built<{A}> built = {|#0:[a]|};"), breaks));
        }
        else
        {
            var other = depth > 0 ? $"DB{id}_{depth}" : explicitMember ? $"IMark{id}" : wrapped is not null ? Src.Wrapped(wrapped, id) : "int";
            var related = depth > 0 || explicitMember;   // a base and a derived type, an interface and an implementer
            var cases = new List<(string F, string S, bool Breaks)>
            {
                ($"D{id}", $"D{id}", true),                                   // one type twice
                (other, $"D{id}", related && attribute == "Unrelated"),
                (rName, rName, true),
                (rName, $"D{id}", false),
            };
            foreach (var (f, s, breaks) in cases)
                foreach (var statement in PairStatements(site))
                    decl.AppendLine(uses.Label(PairHost(position, containing, $"C{id}_{uses.Next}", f, s, statement), breaks));
        }

        var shapes = Src.Usings + Src.WrappedDeclarations(id) + Src.AtSite(r["shape.site"], id, decl.ToString());
        var subjects = TypeArgumentSites.Subjects.Replace("{C}", pair ? "" : constraint);
        var run = await Rz.Analyze("CMTK0010",
        [
            ("Candidates.cs", TypeArgumentKinds.Candidates), ("Subjects.cs", subjects),
            ("Builders.cs", Src.Usings + Builders.Replace("{C}", pair ? "" : constraint) + PairSubjects.Replace("{C}", pair ? constraint : "")),
            ("Core.cs", Src.Usings + uses.Core), ("Shapes.cs", shapes),
        ]);
        if (run.Errors.Count > 0) { r.Outcome("harness-error", "the probe's source does not compile: " + run.ErrorText()); return null; }
        if (run.AnalyzerFailures.Count > 0) return "the analyzer failed: " + string.Join("; ", run.AnalyzerFailures.Select(f => f.GetMessage()));
        r.Note(NoteKeys.Compared, $"{uses.All.Count} uses, {uses.Expected.Count} expected reported");
        return Rz.Agree(run, uses.Expected);
    }

    /// <summary>
    /// A member of shape.containing at shape.position holding <paramref name="statements"/> in the catalogue's frame: a, holder,
    /// bag, crate and o of the argument {A}. A struct is given no instance initializer and no lambda over this.
    /// </summary>
    static string Host(string position, string containing, string name, string argument, string statements)
    {
        statements = statements.Replace("{A}", argument);
        var type = $"GoD<{argument}>";
        string Lambda(string head = "(a, holder, bag, crate, o)", string prelude = "") => $"async {head} => {{ {prelude}{statements} await Task.CompletedTask; }}";
        return Member(position, containing, name, type,
            $"public static async Task Root({argument} a, Holder? holder, Bag<{argument}> bag, Crate<{argument}> crate, object o) {{ {statements} await Task.CompletedTask; }}",
            Lambda(), $"Bag<{argument}> bag0", Lambda("(a, holder, bagX, crate, o)", "var bag = b0; "));
    }

    /// <summary>The pair sites' statements, with x, y and bag2 of the two arguments, as <see cref="Host"/> holds them.</summary>
    static string PairHost(string position, string containing, string name, string first, string second, string statement)
    {
        var statements = statement.Replace("{F}", first).Replace("{S}", second);
        var type = $"GoP<{first}, {second}>";
        string Lambda(string head = "(x, y, bag2)", string prelude = "") => $"async {head} => {{ {prelude}{statements} await Task.CompletedTask; }}";
        return Member(position, containing, name, type,
            $"public static async Task Root({first} x, {second} y, Bag2<{first}, {second}> bag2) {{ {statements} await Task.CompletedTask; }}",
            Lambda(), $"Bag2<{first}, {second}> bag0", Lambda("(x, y, bag2X)", "var bag2 = b0; "));
    }

    static string Member(string position, string containing, string name, string type, string root, string lambda, string primaryParameter, string primaryLambda)
    {
        var self = Src.ContainerSelf(containing, name);
        if (position == "record-ctor-parameter")
        {
            var record = containing is "record-class" or "record-struct";
            return Src.ContainerHead(containing, name, $"({primaryParameter})") +
                $"\n{{\n    public {type} G {{ get {{ var b0 = {primaryParameter.Split(' ')[^1]}; return {primaryLambda}; }} }}\n}}";
        }
        var member = position switch
        {
            "root" => root,
            "property" => $"public static {type} P => {lambda};",
            "nullable-property" => $"public static {type}? N => {lambda};",
            "field" => $"public static readonly {type} F = {lambda};",
            "list-element" => $"public static readonly List<{type}> L = [{lambda}];",
            "array-element" => $"public static readonly {type}[] A = [{lambda}];",
            "immutable-array-element" => $"public static readonly ImmutableArray<{type}> I = [{lambda}];",
            "read-only-list-element" => $"public static readonly IReadOnlyList<{type}> R = [{lambda}];",
            "dictionary-key" => $"public static readonly Dictionary<{type}, int> D = new() {{ [{lambda}] = 1 }};",
            "dictionary-value" => $"public static readonly Dictionary<int, {type}> D = new() {{ [1] = {lambda} }};",
            "required-init" => $"public required {type} K {{ get; init; }}\n    public static {self} Make() => new() {{ K = {lambda} }};",
            "in-patch" => $"public static Patch<{type}> Pa => Patch.Value<{type}>({lambda});",
            "polymorphic-derived-property" => $"public abstract class PB {{ public abstract {type} K {{ get; }} }}\n    public sealed class PD : PB {{ public override {type} K => {lambda}; }}",
            _ => throw new ArgumentException("shape.position " + position),
        };
        return $"{Src.ContainerHead(containing, name)}\n{{\n    {member}\n}}";
    }
}
