using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0010 across the variant catalogue of what a type argument is (<see cref="TypeArgumentKinds"/>):
/// every attribute against every kind, and both pair attributes against every pair, each verdict taken
/// from docs/type-constraints.md.
/// </summary>
/// <remarks>
/// The oracle is the page's tables, written as predicates: "Accepts" over what a type is, "Proved by"
/// over what a type parameter declares. A kind the oracle accepts is reported nowhere; one it rejects is
/// reported once, at the argument (a pair's at the later one).
/// </remarks>
public sealed class TypeArgumentConstraintKindTests
{
    /// <summary>One attribute that constrains a single parameter, read from the page's tables.</summary>
    /// <param name="Attribute">The attribute's name, without the suffix.</param>
    /// <param name="Accepts">The "Accepts" column, over what a type is.</param>
    /// <param name="ProvedBy">The "Proved by" column, over what a type parameter declares; the attribute itself always proves it.</param>
    private sealed record Oracle(string Attribute, Func<Is, bool> Accepts, Func<Parameter, bool> ProvedBy)
    {
        public bool Meets(TypeArgumentKind kind) => kind switch
        {
            KindOfType type           => Accepts(type.Is),
            KindOfParameter parameter => Proves(parameter.Facts),
            _                         => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        public bool Proves(Parameter parameter) => parameter.Carries.Contains(Attribute) || ProvedBy(parameter);
    }

    private static readonly Oracle[] Oracles =
    [
        new("Abstract",
            // an abstract class or record | a concrete type, and an interface
            Accepts: type => type.HasFlag(Is.Abstract) && !type.HasFlag(Is.Interface),
            // nothing but [Abstract]: no C# constraint excludes a concrete type
            ProvedBy: _ => false),

        new("Sealed",
            // a sealed class or record, a struct, an enum | a type that can be derived from
            Accepts: type => type.HasFlag(Is.Sealed) || type.HasFlag(Is.Struct) || type.HasFlag(Is.Enum),
            // struct or unmanaged, [ReadOnlyStruct] or [FlagsEnum], or a bound where T : U with U proving [Sealed]
            ProvedBy: parameter => (parameter.Declares & (Declares.Struct | Declares.Unmanaged)) != 0
                || parameter.Carries.Intersect(["ReadOnlyStruct", "FlagsEnum"]).Any()
                || BoundBy(parameter, "Sealed")),

        new("Record",
            // a record class or record struct | a class or a struct
            Accepts: type => type.HasFlag(Is.Record),
            // a bound that is a record, or a U proving [Record]
            ProvedBy: parameter => parameter.Declares.HasFlag(Declares.RecordBound) || BoundBy(parameter, "Record")),

        new("Concrete",
            // a class, record or struct that can be instantiated, an enum, a delegate | an abstract or static type,
            // an interface, and the abstract bases Enum, Delegate and ValueType
            Accepts: type => !type.HasFlag(Is.Abstract) && !type.HasFlag(Is.Interface),
            // new(), or anything that proves [Sealed], [Sealed] itself included
            ProvedBy: parameter => parameter.Declares.HasFlag(Declares.New) || Single("Sealed").Proves(parameter)),

        new("Interface",
            // an interface | a class, even one that implements the interface
            Accepts: type => type.HasFlag(Is.Interface),
            // nothing but the attribute: an interface bound admits its implementers
            ProvedBy: _ => false),

        new("Unsealed",
            // a class or record class that can be derived from, abstract included, and dynamic, which is object |
            // a sealed or static class, an interface, a struct
            Accepts: type => type.HasFlag(Is.Class) && !type.HasFlag(Is.Sealed) && !type.HasFlag(Is.SpecialBase) || type.HasFlag(Is.Dynamic),
            // nothing but the attribute: class admits a sealed class
            ProvedBy: _ => false),

        new("ValueObject",
            // a struct or class that implements IValue<T> for any T | any other type, a closed set included, and an
            // interface, even one that derives from IValue<T>
            Accepts: type => type.HasFlag(Is.ValueObjectMarker) && !type.HasFlag(Is.Interface),
            // a bound that is or implements the marker, counted only beside struct, unmanaged, new() or a class
            // bound, or a U proving it
            ProvedBy: parameter => parameter.Declares.HasFlag(Declares.ValueObjectBound)
                    && (parameter.Declares & (Declares.Struct | Declares.Unmanaged | Declares.New | Declares.ClassBound)) != 0
                || BoundBy(parameter, "ValueObject")),

        new("ClosedSet",
            // a type that implements IClosedSet<TSelf> | any other type, a value object or an enum included
            Accepts: type => type.HasFlag(Is.ClosedSetMarker),
            // a bound that is or implements the marker, or a U proving it
            ProvedBy: parameter => parameter.Declares.HasFlag(Declares.ClosedSetBound) || BoundBy(parameter, "ClosedSet")),

        new("FlagsEnum",
            // an enum marked [Flags] | a plain enum
            Accepts: type => type.HasFlag(Is.Enum) && type.HasFlag(Is.Flags),
            // nothing but the attribute: Enum admits a plain enum
            ProvedBy: _ => false),

        new("ReadOnlyStruct",
            // a readonly struct or readonly record struct, the primitives included | a mutable struct, an enum, a class
            Accepts: type => type.HasFlag(Is.Struct) && type.HasFlag(Is.ReadOnly),
            // nothing but the attribute: struct admits a mutable one
            ProvedBy: _ => false),
    ];

    /// <summary>
    /// Where CMTK0010 is silent although the oracle rejects, each with the decision. <c>[Unsealed]</c> shipped in
    /// 2.1.0 taking the abstract bases nothing can derive from; refusing them reports more under an Error, which
    /// waits for 3.0.0. The page says so beside the table.
    /// </summary>
    private static readonly Dictionary<(string Attribute, string Kind), string> SilentUntilThree = new()
    {
        [("Unsealed", "the abstract base Enum")]              = "decision 34",
        [("Unsealed", "the abstract base Delegate")]          = "decision 34",
        [("Unsealed", "the abstract base MulticastDelegate")] = "decision 34",
        [("Unsealed", "the abstract base ValueType")]         = "decision 34",
        [("Unsealed", "the abstract base Array")]             = "decision 34",
    };

    /// <summary>The two attributes that constrain a pair: [Unrelated] implies [Distinct].</summary>
    private static readonly Dictionary<string, Apart> PairOracles = new()
    {
        ["Distinct"]  = Apart.Distinct,
        ["Unrelated"] = Apart.Unrelated,
    };

    private static Oracle Single(string attribute) => Oracles.Single(oracle => oracle.Attribute == attribute);

    private static bool BoundBy(Parameter parameter, string attribute) =>
        parameter.BoundBy is { } bound && Single(attribute).Proves(bound);

    /// <summary>
    /// Whether the base package under test declares the attribute. On a current checkout every one does,
    /// which <see cref="Every_constraint_attribute_has_an_oracle_and_every_oracle_an_attribute"/> holds; a
    /// checkout of an earlier commit runs the kinds its attributes allow.
    /// </summary>
    private static bool Declared(string attribute) => RealTypeKit.Assembly.GetType($"CodoMetis.TypeKit.{attribute}Attribute") is not null;

    public static TheoryData<string, string> Kinds()
    {
        var cases = new TheoryData<string, string>();

        foreach (var oracle in Oracles.Where(oracle => Declared(oracle.Attribute)))
        foreach (var kind in TypeArgumentKinds.All.Where(kind => kind.Mentions.All(Declared)))
            cases.Add(oracle.Attribute, kind.Name);

        return cases;
    }

    public static TheoryData<string, string> Pairs()
    {
        var cases = new TheoryData<string, string>();

        foreach (var attribute in PairOracles.Keys.Where(Declared))
        foreach (var pair in TypeArgumentKinds.Pairs.Where(pair => TypeArgumentKinds.Mentions(pair).All(Declared)))
            cases.Add(attribute, pair.Name);

        return cases;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task Each_kind_is_reported_as_the_page_says(string attribute, string kindName)
    {
        var kind     = TypeArgumentKinds.All.Single(candidate => candidate.Name == kindName);
        var breaking = !Single(attribute).Meets(kind) && !SilentUntilThree.ContainsKey((attribute, kind.Name));
        var argument = breaking ? $"{{|#0:{kind.Argument}|}}" : kind.Argument;

        return Run(
            $"public sealed class Subject<[{attribute}] T> {{ }}",
            TypeArgumentKinds.Use(kind.Parameters, kind.Where, "Subject", argument),
            breaking);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task Each_pair_is_reported_as_the_page_says(string attribute, string pairName)
    {
        var pair     = TypeArgumentKinds.Pairs.Single(candidate => candidate.Name == pairName);
        var breaking = pair.Proved < PairOracles[attribute];
        var second   = breaking ? $"{{|#0:{pair.Second}|}}" : pair.Second;

        return Run(
            $"public sealed class Subject<[{attribute}] T1, [{attribute}] T2> {{ }}",
            TypeArgumentKinds.Use(pair.Parameters, pair.Where, "Subject", $"{pair.First}, {second}"),
            breaking);
    }

    [Fact]
    public void Every_constraint_attribute_has_an_oracle_and_every_oracle_an_attribute()
    {
        var declared = RealTypeKit.Assembly.GetExportedTypes()
            .Where(type => type.IsSubclassOf(typeof(Attribute)))
            .Where(type => type.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .Cast<AttributeUsageAttribute>()
                .Any(usage => usage.ValidOn == AttributeTargets.GenericParameter))
            .Select(type => type.Name[..^"Attribute".Length])
            .Order();

        declared.ShouldBe(Oracles.Select(oracle => oracle.Attribute).Concat(PairOracles.Keys).Order());
    }

    [Fact]
    public void The_catalogue_names_are_unique_and_its_pairs_reach_every_verdict()
    {
        TypeArgumentKinds.All.Select(kind => kind.Name).ShouldBeUnique();
        TypeArgumentKinds.Pairs.Select(pair => pair.Name).ShouldBeUnique();

        TypeArgumentKinds.Pairs.Select(pair => pair.Proved).Distinct().Count().ShouldBe(3);

        // A silence is for a kind the oracle rejects; one it accepts needs none.
        SilentUntilThree.Keys.ShouldAllBe(silent => !Single(silent.Attribute).Meets(TypeArgumentKinds.All.Single(kind => kind.Name == silent.Kind)));
    }

    private static Task Run(string subject, string use, bool breaking)
    {
        var test = new AnalyzerTest<TypeArgumentConstraintAnalyzer>(TypeArgumentKinds.Candidates + subject);
        test.TestBehaviors = VariantSweep.Behaviors;
        test.TestState.Sources.Add(("Use.cs", use));

        if (breaking)
            test.ExpectedDiagnostics.Add(new DiagnosticResult("CMTK0010", DiagnosticSeverity.Error).WithLocation(0));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
