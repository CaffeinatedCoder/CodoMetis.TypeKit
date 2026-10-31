namespace CodoMetis.TypeKit.Analyzers.Tests.Variants;

/// <summary>What a closed type argument is, as its declaration says.</summary>
[Flags]
public enum Is
{
    Class = 1 << 0,
    Record = 1 << 1,
    Struct = 1 << 2,
    Enum = 1 << 3,
    Delegate = 1 << 4,
    Interface = 1 << 5,
    Array = 1 << 6,
    Dynamic = 1 << 7,
    Abstract = 1 << 8,
    Sealed = 1 << 9,
    ReadOnly = 1 << 10,
    Flags = 1 << 11,

    /// <summary><c>Enum</c>, <c>Delegate</c>, <c>MulticastDelegate</c>, <c>ValueType</c> and <c>Array</c>: abstract classes no type written in C# derives from (CS0644).</summary>
    SpecialBase = 1 << 12,

    /// <summary>Implements <c>IValue&lt;T&gt;</c> for some <c>T</c>, or is it.</summary>
    ValueObjectMarker = 1 << 13,

    /// <summary>Implements <c>IClosedSet&lt;TSelf&gt;</c>.</summary>
    ClosedSetMarker = 1 << 14,
}

/// <summary>What a type parameter given as the argument declares in its <c>where</c> clause.</summary>
[Flags]
public enum Declares
{
    Nothing = 0,
    Class = 1 << 0,
    Struct = 1 << 1,
    Unmanaged = 1 << 2,
    New = 1 << 3,

    /// <summary>A class type as a bound, <c>Enum</c> and <c>Delegate</c> included.</summary>
    ClassBound = 1 << 4,

    /// <summary><c>where T : Enum</c>, which every enum meets, and <c>Enum</c> itself.</summary>
    EnumBound = 1 << 5,

    /// <summary>A record as a bound; only records derive from records (CS8865).</summary>
    RecordBound = 1 << 6,

    InterfaceBound = 1 << 7,

    /// <summary><c>IValue&lt;T&gt;</c> or an interface deriving from it, as a bound.</summary>
    ValueObjectBound = 1 << 8,

    /// <summary><c>IClosedSet&lt;T&gt;</c> as a bound.</summary>
    ClosedSetBound = 1 << 9,
}

/// <summary>
/// A type parameter given as the argument: its constraints, the attributes it carries, and the
/// parameter it is bound by (<c>where T : U</c>), if any.
/// </summary>
public sealed record Parameter(Declares Declares, params string[] Carries)
{
    public Parameter? BoundBy { get; init; }
}

/// <summary>One kind of type a type argument can be.</summary>
/// <param name="Name">What the argument is, as a test case names it.</param>
/// <param name="Argument">The argument as written.</param>
/// <param name="Parameters">The type parameter list of the generic the argument is written in, empty for none.</param>
/// <param name="Where">That generic's constraint clauses.</param>
public abstract record TypeArgumentKind(string Name, string Argument, string Parameters, string Where)
{
    /// <summary>The attributes the kind's own declaration carries, which a base package must declare for it to compile.</summary>
    public virtual IEnumerable<string> Mentions => [];

    public override string ToString() => Name;
}

/// <summary>A type whatever its type parameters are: a closed type, or one built from a parameter.</summary>
public sealed record KindOfType(string Name, string Argument, Is Is, string Parameters = "", string Where = "")
    : TypeArgumentKind(Name, Argument, Parameters, Where);

/// <summary>A type parameter of the generic the argument is written in, <c>T</c>.</summary>
public sealed record KindOfParameter(string Name, string Parameters, string Where, Parameter Facts)
    : TypeArgumentKind(Name, "T", Parameters, Where)
{
    public override IEnumerable<string> Mentions => Walk(Facts).SelectMany(parameter => parameter.Carries);

    private static IEnumerable<Parameter> Walk(Parameter? parameter)
    {
        for (; parameter is not null; parameter = parameter.BoundBy) yield return parameter;
    }
}

/// <summary>What docs/type-constraints.md proves of a pair: nothing, that its two are never one type, or never related.</summary>
public enum Apart
{
    Unproved,
    Distinct,
    Unrelated,
}

/// <summary>Two arguments given to two parameters that constrain each other.</summary>
/// <param name="Name">What the pair is, as a test case names it.</param>
/// <param name="First">The argument for the first parameter.</param>
/// <param name="Second">The argument for the second, where a breach is reported.</param>
/// <param name="Proved">What the docs' rules prove of the pair; <c>[Unrelated]</c> implies <c>[Distinct]</c>.</param>
/// <param name="Because">The rule of docs/type-constraints.md that decides <paramref name="Proved"/>.</param>
/// <param name="Parameters">The type parameter list of the generic the pair is written in, empty for none.</param>
/// <param name="Where">That generic's constraint clauses.</param>
public sealed record PairKind(string Name, string First, string Second, Apart Proved, string Because, string Parameters = "", string Where = "")
{
    public override string ToString() => Name;
}

/// <summary>
/// The variant catalogue for analyzer input shape (AIS) when the input is a type argument, on its second
/// axis (design/adr/0095): what the argument is. <see cref="TypeArgumentSites"/> is the first, where it
/// is written.
/// </summary>
/// <remarks>
/// <para>
/// Seven of CMTK0010's fourteen fixes of 2026-10-13 and 14 (design/fix-history.md, chain A) were about
/// what an argument is, not where: an enum and a delegate taken as concrete, a type parameter whose
/// constraints prove the attribute, a bound or an argument built from a type parameter, an <c>Enum</c>
/// bound against <c>struct</c>, an interface deriving from the value-object marker, <c>dynamic</c>.
/// </para>
/// <para>
/// A kind states what the type is (<see cref="Is"/>) or what the parameter declares
/// (<see cref="Declares"/>), as the language has it, and leaves the verdict to an oracle per attribute
/// the rule supplies. A pair states what docs/type-constraints.md proves of it, and the rule it follows
/// from, since what proves two arguments apart is that page's rule and not the language's.
/// </para>
/// <para>
/// Each kind is written at one site, a field's type, since the sites are the other axis. A kind the
/// language adds goes here.
/// </para>
/// </remarks>
public static class TypeArgumentKinds
{
    /// <summary>The types the kinds name, a file of their own.</summary>
    public const string Candidates =
        """
        global using System;
        global using System.Collections.Generic;
        global using CodoMetis.TypeKit;

        public class Widget { }
        public abstract class Handler { }
        public interface IHandler { }
        public sealed class OrderHandler : Handler, IHandler { }
        public class Tracked : Handler { }
        public record Note(int Id);
        public sealed record Placed(int Id);
        public abstract record Command;
        public struct Mutable { public int Value; }
        public readonly struct Frozen { }
        public record struct Spot(int X);
        public readonly record struct Stamp(int X);
        public enum Shade { Light, Dark }
        [Flags] public enum Rights { None = 0, Read = 1, Write = 2 }
        public delegate void Notify();
        public class Node<T> { }
        public sealed class Leaf : Node<Leaf> { }

        public readonly struct Id : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
        public class IdClass : CodoMetis.TypeKit.ValueObjects.IValue<int> { }
        public interface IDerivedValue : CodoMetis.TypeKit.ValueObjects.IValue<int> { }

        public sealed class Palette : CodoMetis.TypeKit.ClosedSets.IClosedSet<Palette>
        {
            public string Name => "";
            public string QualifiedName => "";
            public static IReadOnlyList<Palette> All => [];
            public static bool TryParse(string? name, out Palette member) { member = null!; return false; }
        }

        """;

    private const string ValueMarker = "CodoMetis.TypeKit.ValueObjects.IValue<int>";

    /// <summary>A file that writes <paramref name="generic"/> closed over <paramref name="arguments"/> as a field's type.</summary>
    public static string Use(string parameters, string where, string generic, string arguments) =>
        $$"""
          #nullable enable
          public static class Use{{parameters}} {{where}}
          {
              public static {{generic}}<{{arguments}}>? Field;
          }
          """;

    public static IReadOnlyList<TypeArgumentKind> All { get; } =
    [
        // Declared in source.
        new KindOfType("a class", "Widget", Is.Class),
        new KindOfType("an abstract class", "Handler", Is.Class | Is.Abstract),
        new KindOfType("a sealed class", "OrderHandler", Is.Class | Is.Sealed),
        new KindOfType("a record", "Note", Is.Class | Is.Record),
        new KindOfType("a sealed record", "Placed", Is.Class | Is.Record | Is.Sealed),
        new KindOfType("an abstract record", "Command", Is.Class | Is.Record | Is.Abstract),
        new KindOfType("a struct", "Mutable", Is.Struct),
        new KindOfType("a readonly struct", "Frozen", Is.Struct | Is.ReadOnly),
        new KindOfType("a record struct", "Spot", Is.Struct | Is.Record),
        new KindOfType("a readonly record struct", "Stamp", Is.Struct | Is.Record | Is.ReadOnly),
        new KindOfType("an enum", "Shade", Is.Enum),
        new KindOfType("a flags enum", "Rights", Is.Enum | Is.Flags),
        new KindOfType("a delegate", "Notify", Is.Delegate | Is.Sealed),
        new KindOfType("an interface", "IHandler", Is.Interface),
        new KindOfType("a value object struct", "Id", Is.Struct | Is.ReadOnly | Is.ValueObjectMarker),
        new KindOfType("a value object class", "IdClass", Is.Class | Is.ValueObjectMarker),
        new KindOfType("an interface deriving from the value-object marker", "IDerivedValue", Is.Interface | Is.ValueObjectMarker),
        new KindOfType("the value-object marker", ValueMarker, Is.Interface | Is.ValueObjectMarker),
        new KindOfType("a closed set", "Palette", Is.Class | Is.Sealed | Is.ClosedSetMarker),

        // Declared in metadata, or by the language.
        new KindOfType("a primitive", "int", Is.Struct | Is.ReadOnly),
        new KindOfType("a sealed class from metadata", "string", Is.Class | Is.Sealed),
        new KindOfType("an abstract class from metadata", "System.IO.Stream", Is.Class | Is.Abstract),
        new KindOfType("a readonly struct from metadata", "Guid", Is.Struct | Is.ReadOnly),
        new KindOfType("an enum from metadata", "DayOfWeek", Is.Enum),
        new KindOfType("a flags enum from metadata", "System.IO.FileAccess", Is.Enum | Is.Flags),
        new KindOfType("a generic class closed over a struct", "List<int>", Is.Class),
        new KindOfType("object", "object", Is.Class),
        new KindOfType("dynamic", "dynamic", Is.Dynamic),
        new KindOfType("a nullable reference type", "string?", Is.Class | Is.Sealed),
        new KindOfType("a nullable value type", "int?", Is.Struct),
        new KindOfType("a tuple", "(int, string)", Is.Struct),
        new KindOfType("an array", "int[]", Is.Array | Is.Sealed),
        new KindOfType("a jagged array of an abstract class", "Handler[][]", Is.Array | Is.Sealed),
        new KindOfType("the abstract base Enum", "Enum", Is.Class | Is.Abstract | Is.SpecialBase),
        new KindOfType("the abstract base Delegate", "Delegate", Is.Class | Is.Abstract | Is.SpecialBase),
        new KindOfType("the abstract base ValueType", "ValueType", Is.Class | Is.Abstract | Is.SpecialBase),
        new KindOfType("the abstract base MulticastDelegate", "MulticastDelegate", Is.Class | Is.Abstract | Is.SpecialBase),
        new KindOfType("the abstract base Array", "Array", Is.Class | Is.Abstract | Is.SpecialBase),

        // Built from a type parameter: what it is does not depend on the parameter.
        new KindOfType("a class built from a parameter", "List<T>", Is.Class, "<T>"),
        new KindOfType("an array built from a parameter", "T[]", Is.Array | Is.Sealed, "<T>"),
        new KindOfType("a nullable built from a struct parameter", "T?", Is.Struct, "<T>", "where T : struct"),
        new KindOfType("a delegate built from a parameter", "Func<T>", Is.Delegate | Is.Sealed, "<T>"),
        new KindOfType("a tuple built from a parameter", "(T, int)", Is.Struct, "<T>"),

        // A type parameter: what every closed use of it can be.
        new KindOfParameter("an unconstrained parameter", "<T>", "", new(Declares.Nothing)),
        new KindOfParameter("a class-constrained parameter", "<T>", "where T : class", new(Declares.Class)),
        new KindOfParameter("a struct-constrained parameter", "<T>", "where T : struct", new(Declares.Struct)),
        new KindOfParameter("an unmanaged-constrained parameter", "<T>", "where T : unmanaged", new(Declares.Unmanaged)),
        new KindOfParameter("a new()-constrained parameter", "<T>", "where T : new()", new(Declares.New)),
        new KindOfParameter("an Enum-bound parameter", "<T>", "where T : Enum", new(Declares.ClassBound | Declares.EnumBound)),
        new KindOfParameter("a struct, Enum parameter", "<T>", "where T : struct, Enum", new(Declares.Struct | Declares.ClassBound | Declares.EnumBound)),
        new KindOfParameter("a Delegate-bound parameter", "<T>", "where T : Delegate", new(Declares.ClassBound)),
        new KindOfParameter("a parameter bound by an abstract class", "<T>", "where T : Handler", new(Declares.ClassBound)),
        new KindOfParameter("a parameter bound by an abstract record", "<T>", "where T : Command", new(Declares.ClassBound | Declares.RecordBound)),
        new KindOfParameter("a parameter bound by an interface", "<T>", "where T : IHandler", new(Declares.InterfaceBound)),
        new KindOfParameter("a parameter bound by IEquatable<T>", "<T>", "where T : IEquatable<T>", new(Declares.InterfaceBound)),
        new KindOfParameter("a parameter bound by Node<T>", "<T>", "where T : Node<T>", new(Declares.ClassBound)),
        new KindOfParameter("a parameter bound by IValue<int>", "<T>", $"where T : {ValueMarker}", new(Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a struct parameter bound by IValue<int>", "<T>", $"where T : struct, {ValueMarker}", new(Declares.Struct | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("an unmanaged parameter bound by IValue<int>", "<T>", $"where T : unmanaged, {ValueMarker}", new(Declares.Unmanaged | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a class parameter bound by IValue<int>", "<T>", $"where T : class, {ValueMarker}", new(Declares.Class | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a new() parameter bound by IValue<int>", "<T>", $"where T : {ValueMarker}, new()", new(Declares.New | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a new() parameter bound by an interface deriving from IValue<int>", "<T>", "where T : IDerivedValue, new()", new(Declares.New | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a parameter bound by a class and IValue<int>", "<T>", $"where T : Handler, {ValueMarker}", new(Declares.ClassBound | Declares.InterfaceBound | Declares.ValueObjectBound)),
        new KindOfParameter("a parameter bound by IClosedSet<T>", "<T>", "where T : class, CodoMetis.TypeKit.ClosedSets.IClosedSet<T>", new(Declares.Class | Declares.InterfaceBound | Declares.ClosedSetBound)),
        new KindOfParameter("T : U, U carrying [Sealed]", "<[Sealed] U, T>", "where T : U", new(Declares.Nothing) { BoundBy = new(Declares.Nothing, "Sealed") }),
        new KindOfParameter("T : U, U bound by an abstract record", "<U, T>", "where U : Command where T : U", new(Declares.Nothing) { BoundBy = new(Declares.ClassBound | Declares.RecordBound) }),
        new KindOfParameter("T : U, U bound by IValue<int>", "<U, T>", $"where U : {ValueMarker} where T : U", new(Declares.Nothing) { BoundBy = new(Declares.InterfaceBound | Declares.ValueObjectBound) }),
        new KindOfParameter("T : U, U bound by a class and IValue<int>", "<U, T>", $"where U : Handler, {ValueMarker} where T : U", new(Declares.Nothing) { BoundBy = new(Declares.ClassBound | Declares.InterfaceBound | Declares.ValueObjectBound) }),
        new KindOfParameter("a struct parameter carrying [ReadOnlyStruct]", "<[ReadOnlyStruct] T>", "where T : struct", new(Declares.Struct, "ReadOnlyStruct")),
        new KindOfParameter("a struct, Enum parameter carrying [FlagsEnum]", "<[FlagsEnum] T>", "where T : struct, Enum", new(Declares.Struct | Declares.ClassBound | Declares.EnumBound, "FlagsEnum")),
        .. Carrying("Abstract", "Sealed", "Record", "Concrete", "Interface", "Unsealed", "ValueObject", "ClosedSet", "FlagsEnum", "ReadOnlyStruct"),
    ];

    /// <summary>A parameter carrying each attribute, and nothing else.</summary>
    private static IEnumerable<TypeArgumentKind> Carrying(params string[] attributes) =>
        attributes.Select(attribute => new KindOfParameter($"a parameter carrying [{attribute}]", $"<[{attribute}] T>", "", new(Declares.Nothing, attribute)));

    public static IReadOnlyList<PairKind> Pairs { get; } =
    [
        // Two closed types: one type, related or neither.
        new("two unrelated types", "int", "string", Apart.Unrelated, "neither converts to the other"),
        new("one type twice", "Placed", "Placed", Apart.Unproved, "one type twice"),
        new("one type twice, one annotated", "string?", "string", Apart.Unproved, "one type twice, nullable annotations aside"),
        new("a base class and a derived one", "Handler", "OrderHandler", Apart.Distinct, "a base and a derived type"),
        new("a derived class and its base", "OrderHandler", "Handler", Apart.Distinct, "a base and a derived type"),
        new("an interface and an implementer", "IHandler", "OrderHandler", Apart.Distinct, "an interface and an implementer"),
        new("object and a struct", "object", "int", Apart.Distinct, "object and anything"),
        new("a struct and ValueType", "int", "ValueType", Apart.Distinct, "a struct and ValueType"),
        new("an enum and Enum", "Shade", "Enum", Apart.Distinct, "an implicit boxing conversion"),
        new("two arrays related by covariance", "string[]", "object[]", Apart.Distinct, "an implicit reference conversion"),
        new("two delegates related by variance", "Func<string>", "Func<object>", Apart.Distinct, "an implicit reference conversion"),
        new("a struct and its nullable", "int", "int?", Apart.Unrelated, "no implicit identity, reference or boxing conversion between them"),
        new("dynamic and object", "dynamic", "object", Apart.Unproved, "dynamic, which is object"),

        // A type parameter against another or against a type.
        new("one parameter twice", "T", "T", Apart.Unproved, "one type twice", "<T>"),
        new("two unconstrained parameters", "TA", "TB", Apart.Unproved, "nothing proves them apart", "<TA, TB>"),
        new("two parameters carrying [Distinct]", "TA", "TB", Apart.Distinct, "both parameters carry [Distinct]", "<[Distinct] TA, [Distinct] TB>"),
        new("two parameters carrying [Unrelated]", "TA", "TB", Apart.Unrelated, "both parameters carry [Unrelated]", "<[Unrelated] TA, [Unrelated] TB>"),
        new("a struct parameter and a class parameter", "TS", "TC", Apart.Distinct, "struct against class proves [Distinct]; class alone proves nothing for [Unrelated]", "<TS, TC>", "where TS : struct where TC : class"),
        new("a struct parameter and a class-bound parameter", "TS", "TC", Apart.Unrelated, "struct against a class bound other than object, ValueType and Enum", "<TS, TC>", "where TS : struct where TC : Handler"),
        new("an unmanaged parameter and a class-bound parameter", "TS", "TC", Apart.Unrelated, "unmanaged against a class bound other than object, ValueType and Enum", "<TS, TC>", "where TS : unmanaged where TC : Handler"),
        new("an Enum-bound parameter and a struct parameter", "TE", "TS", Apart.Unproved, "a class bound other than Enum, which every enum meets", "<TE, TS>", "where TE : Enum where TS : struct"),
        new("parameters bound by unrelated classes", "TA", "TB", Apart.Unrelated, "both have class bounds, and neither derives from the other", "<TA, TB>", "where TA : Handler where TB : Widget"),
        new("parameters bound by a base class and a derived one", "TA", "TB", Apart.Unproved, "class bounds where one derives from the other", "<TA, TB>", "where TA : Handler where TB : Tracked"),
        new("parameters bound by one class built from each", "TA", "TB", Apart.Unproved, "Node<TA> and Node<TB> are one chain", "<TA, TB>", "where TA : Node<TA> where TB : Node<TB>"),
        new("a parameter and a type it admits", "T", "int", Apart.Unproved, "nothing proves them apart", "<T>"),
        new("a class parameter and a struct", "T", "int", Apart.Distinct, "against a type, the type breaks the parameter's constraints; class alone proves nothing for [Unrelated]", "<T>", "where T : class"),
        new("a struct parameter and a class", "T", "string", Apart.Unrelated, "struct against a class other than object, ValueType and Enum", "<T>", "where T : struct"),
        new("a struct parameter and ValueType", "T", "ValueType", Apart.Distinct, "against a type, the type breaks the parameter's constraints; ValueType is excluded for [Unrelated]", "<T>", "where T : struct"),
        new("an Enum-bound parameter and Enum", "T", "Enum", Apart.Unproved, "where T : Enum admits Enum", "<T>", "where T : Enum"),
        new("a parameter bound by IEquatable<T>, and int", "T", "int", Apart.Unproved, "where T : IEquatable<T> admits int", "<T>", "where T : IEquatable<T>"),
        new("a parameter bound by Node<T>, and Leaf", "T", "Leaf", Apart.Unproved, "a class bound built from a parameter is met by a type whose chain holds its definition", "<T>", "where T : Node<T>"),

        // Types built from a type parameter, held as the parameter is.
        new("List<T> and List<int>", "List<T>", "List<int>", Apart.Unproved, "List<T> is held as T is", "<T>"),
        new("List<T> and List<int>, T : class", "List<T>", "List<int>", Apart.Distinct, "an argument no use can make the other's; one chain for [Unrelated]", "<T>", "where T : class"),
        new("List<T> and HashSet<int>", "List<T>", "HashSet<int>", Apart.Unrelated, "another definition; two classes of different chains", "<T>"),
        new("T[] and int[]", "T[]", "int[]", Apart.Unproved, "T[] is held as T is", "<T>"),
        new("IEnumerable<T> and List<int>", "IEnumerable<T>", "List<int>", Apart.Distinct, "another definition; an interface never proves [Unrelated]", "<T>"),
        new("List<TA> and List<TB>", "List<TA>", "List<TB>", Apart.Unproved, "List<TA> and List<TB> are one when TA and TB are", "<TA, TB>"),
    ];

    /// <summary>The attributes a pair kind's declaration carries.</summary>
    public static IEnumerable<string> Mentions(PairKind pair) =>
        new[] { "Distinct", "Unrelated" }.Where(attribute => pair.Parameters.Contains($"[{attribute}]"));
}
