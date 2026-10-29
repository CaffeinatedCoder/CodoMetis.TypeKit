namespace CodoMetis.TypeKit.Analyzers.Tests.Variants;

/// <summary>
/// What a call to a TypeKit lookup is made on: how the receiver is typed, and how the call is written.
/// </summary>
/// <param name="Name">What the receiver is, as a test case names it.</param>
/// <param name="Parameter">The run method's parameter that holds it, <c>{A}</c> the element type.</param>
/// <param name="Call">
/// The call as written: <c>{Class}</c> is <c>Option</c> or <c>Result</c>, <c>{M}</c> the method,
/// <c>{Args}</c> its arguments, and <c>[[</c> <c>]]</c> enclose the invocation a diagnostic reports.
/// </param>
/// <param name="IsQuery">Whether the receiver is an <c>IQueryable&lt;T&gt;</c> before the compiler converts it.</param>
/// <param name="TypeParameters">The run method's type parameters, empty for none.</param>
/// <param name="Where">Their constraint clauses.</param>
public sealed record QueryReceiver(string Name, string Parameter, string Call, bool IsQuery, string TypeParameters = "", string Where = "")
{
    public override string ToString() => Name;
}

/// <summary>What a condition converts to.</summary>
public enum Converts
{
    /// <summary>A <c>Func</c> alone, so a query's overload cannot take it.</summary>
    ToDelegate,

    /// <summary>A <c>Func</c> or an expression: a lambda, which binds to the query's overload where there is one.</summary>
    ToEither,

    /// <summary>An expression alone, which a sequence's overload cannot take.</summary>
    ToExpression,
}

/// <summary>A condition passed to a lookup, written in the run method's body.</summary>
/// <param name="Name">What the condition is, as a test case names it.</param>
/// <param name="Argument">The argument as written; the members it names are <see cref="QueryDelegateShapes.Members"/>.</param>
/// <param name="Converts">What it converts to.</param>
public sealed record QueryCondition(string Name, string Argument, Converts Converts)
{
    public bool IsDelegate => Converts == Converts.ToDelegate;

    public override string ToString() => Name;
}

/// <summary>One of the lookups that has an overload on a sequence, taking a <c>Func</c>, and one on a query.</summary>
/// <param name="Name">The method.</param>
/// <param name="Class">The static class that declares it.</param>
/// <param name="After">The arguments after the condition.</param>
/// <param name="HasQueryOverload">
/// Whether its query overload compiles: <c>LastOrNone</c>'s and <c>LastOrError</c>'s refuse to, so a
/// lambda at the call on a query is no case.
/// </param>
public sealed record QueryLookup(string Name, string Class, string After, bool HasQueryOverload)
{
    public override string ToString() => Name;
}

/// <summary>
/// The variant catalogue for CMTK0009's query condition (design/adr/0095): a lookup called on a query
/// with a condition that is a delegate binds to its sequence overload, and reads every row.
/// </summary>
/// <remarks>
/// <para>
/// Three axes. The receiver: how a query can be typed (an interface, one deriving from it, a class
/// implementing it, a type parameter constrained to it, EF Core's <c>DbSet</c> in its own test) and the
/// call shapes of <see cref="TypeArgumentSites"/> that move the receiver (a null-conditional call, the
/// lowered static form, a call in a lambda), against sequences that are no query. The condition: every
/// way C# writes a delegate, against the forms that convert to an expression. The element type is
/// <see cref="TypeArgumentKinds"/>.
/// </para>
/// <para>
/// A receiver typed as a sequence is no query to the rule, even where it holds one: the call cannot
/// bind otherwise, and <c>AsEnumerable()</c> or a cast says the full read is meant.
/// </para>
/// </remarks>
public static class QueryDelegateShapes
{
    /// <summary>The members the conditions name, declared in the class that holds the run method.</summary>
    public const string Members =
        """
            private static Func<{A}, bool> field = x => true;
            private static Func<{A}, bool> Property => x => true;
            private static readonly Expression<Func<{A}, bool>> expressionField = x => true;
            private static bool Test({A} x) => true;
            private bool InstanceTest({A} x) => true;
            private static Func<{A}, bool> Make() => x => true;
        """;

    /// <summary>The locals the conditions name, declared at the top of the run method.</summary>
    public const string Locals =
        """
                Func<{A}, bool> local = x => true;
                Expression<Func<{A}, bool>> expressionLocal = x => true;
                static bool LocalTest({A} x) => true;
        """;

    /// <summary>The declarations the receivers name, a file of their own.</summary>
    public const string Declarations =
        """
        using System.Collections;
        using System.Linq;
        using System.Linq.Expressions;

        public abstract class Table<T> : IQueryable<T>
        {
            public abstract Type ElementType { get; }
            public abstract Expression Expression { get; }
            public abstract IQueryProvider Provider { get; }
            public abstract IEnumerator<T> GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public interface IRepository<T> : IQueryable<T> { }

        """;

    public static IReadOnlyList<QueryLookup> Lookups { get; } =
    [
        new("FirstOrNone", "Option", "", HasQueryOverload: true),
        new("SingleOrNone", "Option", "", HasQueryOverload: true),
        new("FirstOrError", "Result", ", \"none\"", HasQueryOverload: true),
        new("LastOrNone", "Option", "", HasQueryOverload: false),
        new("LastOrError", "Result", ", \"none\"", HasQueryOverload: false),
    ];

    private const string Instance = "_ = [[source.{M}({Args})]];";

    public static IReadOnlyList<QueryReceiver> Receivers { get; } =
    [
        // A query, as it can be typed.
        new("an IQueryable<T>", "IQueryable<{A}> source", Instance, IsQuery: true),
        new("an IOrderedQueryable<T>", "IOrderedQueryable<{A}> source", Instance, IsQuery: true),
        new("an EnumerableQuery<T>, a class from metadata", "EnumerableQuery<{A}> source", Instance, IsQuery: true),
        new("a class that implements IQueryable<T>", "Table<{A}> source", Instance, IsQuery: true),
        new("an interface that derives from IQueryable<T>", "IRepository<{A}> source", Instance, IsQuery: true),
        new("a type parameter constrained to IQueryable<T>", "TQuery source", Instance, IsQuery: true, "<TQuery>", "where TQuery : IQueryable<{A}>"),
        new("a type parameter bound by one constrained to IQueryable<T>", "TQuery source", Instance, IsQuery: true, "<TBase, TQuery>", "where TBase : IQueryable<{A}> where TQuery : TBase"),

        // A query, as the call is written.
        new("a composed query", "IQueryable<{A}> source", "_ = [[source.Where(x => true).{M}({Args})]];", IsQuery: true),
        new("a list made a query", "List<{A}> source", "_ = [[source.AsQueryable().{M}({Args})]];", IsQuery: true),
        new("a parenthesized receiver", "IQueryable<{A}> source", "_ = [[(source).{M}({Args})]];", IsQuery: true),
        new("a null-conditional call", "IQueryable<{A}> source", "_ = source?[[.{M}({Args})]];", IsQuery: true),
        new("the lowered static form", "IQueryable<{A}> source", "_ = [[{Class}.{M}(source, {Args})]];", IsQuery: true),
        new("a call in a lambda", "IQueryable<{A}> source", "Action act = () => _ = [[source.{M}({Args})]];", IsQuery: true),
        new("a query held in a local", "IQueryable<{A}> source", "var held = source.Where(x => true);\n_ = [[held.{M}({Args})]];", IsQuery: true),

        // No query.
        new("an IEnumerable<T>", "IEnumerable<{A}> source", Instance, IsQuery: false),
        new("a List<T>", "List<{A}> source", Instance, IsQuery: false),
        new("an array", "{A}[] source", Instance, IsQuery: false),
        new("a type parameter constrained to IEnumerable<T>", "TSequence source", Instance, IsQuery: false, "<TSequence>", "where TSequence : IEnumerable<{A}>"),
        new("a query read through AsEnumerable()", "IQueryable<{A}> source", "_ = [[source.AsEnumerable().{M}({Args})]];", IsQuery: false),
        new("a query cast to IEnumerable<T>", "IQueryable<{A}> source", "_ = [[((IEnumerable<{A}>)source).{M}({Args})]];", IsQuery: false),
        new("a query held as IEnumerable<T>", "IQueryable<{A}> source", "IEnumerable<{A}> held = source;\n_ = [[held.{M}({Args})]];", IsQuery: false),
        new("a sequence in the lowered static form", "IEnumerable<{A}> source", "_ = [[{Class}.{M}(source, {Args})]];", IsQuery: false),
    ];

    public static IReadOnlyList<QueryCondition> Conditions { get; } =
    [
        new("a Func local", "local", Converts.ToDelegate),
        new("a Func parameter", "parameter", Converts.ToDelegate),
        new("a Func field", "field", Converts.ToDelegate),
        new("a Func property", "Property", Converts.ToDelegate),
        new("a static method group", "Test", Converts.ToDelegate),
        new("an instance method group", "InstanceTest", Converts.ToDelegate),
        new("a local function's method group", "LocalTest", Converts.ToDelegate),
        new("an anonymous method", "delegate ({A} x) { return true; }", Converts.ToDelegate),
        new("a call that returns a Func", "Make()", Converts.ToDelegate),
        new("a conditional of two Funcs", "flag ? local : field", Converts.ToDelegate),
        new("a lambda cast to a Func", "(Func<{A}, bool>)(x => true)", Converts.ToDelegate),
        new("an expression compiled", "expressionField.Compile()", Converts.ToDelegate),

        new("a lambda at the call", "x => true", Converts.ToEither),
        new("a static lambda", "static x => true", Converts.ToEither),
        new("a lambda with a typed parameter", "({A} x) => true", Converts.ToEither),
        new("an expression local", "expressionLocal", Converts.ToExpression),
        new("an expression field", "expressionField", Converts.ToExpression),
    ];

    /// <summary>
    /// A file whose run method calls every lookup on <paramref name="receiver"/> with every condition, over
    /// elements written <paramref name="element"/>, and the calls a diagnostic is expected at, by markup location.
    /// </summary>
    public static (string Source, IReadOnlyList<(int Location, QueryLookup Lookup, QueryCondition Condition)> Expected) Use(
        QueryReceiver receiver, string element, string parameters = "", string where = "")
    {
        var expected = new List<(int, QueryLookup, QueryCondition)>();
        var body     = new System.Text.StringBuilder();

        foreach (var lookup in Lookups)
        foreach (var condition in Conditions)
        {
            // A call that does not compile is no case: an expression on a sequence, and on a query a lambda
            // or an expression given to a lookup whose query overload refuses to compile.
            if (condition.Converts == Converts.ToExpression && !receiver.IsQuery) continue;
            if (!condition.IsDelegate && receiver.IsQuery && !lookup.HasQueryOverload) continue;

            var reported = receiver.IsQuery && condition.IsDelegate;
            var call     = receiver.Call.Replace("{Class}", lookup.Class).Replace("{M}", lookup.Name).Replace("{Args}", condition.Argument + lookup.After);

            call = reported
                ? call.Replace("[[", $"{{|#{expected.Count}:").Replace("]]", "|}")
                : call.Replace("[[", "").Replace("]]", "");

            if (reported) expected.Add((expected.Count, lookup, condition));

            body.Append("        {\n            ").Append(call.Replace("\n", "\n            ")).Append("\n        }\n");
        }

        var source =
            $$"""
              using System.Linq;
              using System.Linq.Expressions;

              public sealed class Use{{parameters}} {{where}}
              {
              {{Members}}

                  public void Run{{receiver.TypeParameters}}({{receiver.Parameter}}, Func<{A}, bool> parameter, bool flag) {{receiver.Where}}
                  {
              {{Locals}}
              {{body}}
                  }
              }
              """;

        return (source.Replace("{A}", element), expected);
    }
}
