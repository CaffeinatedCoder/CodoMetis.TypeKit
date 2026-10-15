namespace CodoMetis.TypeKit.Analyzers.Tests.Variants;

/// <summary>What a rule that judges a type argument does at a site.</summary>
public enum Reach
{
    /// <summary>The argument reaches the type parameter: a breaking one is reported once, at the site's <c>{|#0:…|}</c> span.</summary>
    Use,

    /// <summary>The generic is named and no argument is used (a cref, a <c>nameof</c>, an unbound <c>typeof</c>, an error type): never reported.</summary>
    Mention,

    /// <summary>Generated code that maps to no markup: never reported (<c>GeneratedCode.Report</c>).</summary>
    Hidden,
}

/// <summary>One shape of source through which a type argument reaches a type parameter.</summary>
/// <param name="Name">What the site is, as a test case names it.</param>
/// <param name="Reach">What a rule reports there.</param>
/// <param name="Use">
/// The source of the site, a file of its own. <c>{C}</c> is the constraint the rule reads on the
/// parameter, <c>{A}</c> the type argument. Statements go through <see cref="TypeArgumentSites.Body"/>,
/// whose method has <c>a</c> of type <c>{A}</c> and the subjects' receivers in scope.
/// </param>
/// <param name="File">The file's name, which decides whether the compiler takes it for generated code.</param>
public sealed record TypeArgumentSite(string Name, Reach Reach, string Use, string File = "Use.cs")
{
    public override string ToString() => Name;
}

/// <summary>
/// The variant catalogue for analyzer input shape (AIS) when the input is a type argument
/// (design/adr/0095): every shape of source through which a type argument reaches a type parameter.
/// </summary>
/// <remarks>
/// <para>
/// A fix to a rule that judges type arguments runs here, so that it is tested across the class of
/// input its defect lives in and not only on the one input a review found. CMTK0010's fourteen fixes of
/// 2026-10-13 and 14 (design/fix-history.md, chain A) were found one shape at a time: a method group,
/// then crefs and implementations, then extension blocks and generic attributes, then a null-conditional
/// call and <c>nameof</c>, then generated code.
/// </para>
/// <para>
/// Each site is a language form, not a CMTK0010 test: the catalogue declares its generic subjects with a
/// constraint <c>{C}</c> the rule supplies, and is run with an argument that breaks it and one that meets
/// it, so a silent site and a false report are both caught. A shape the language adds goes here, for
/// every rule at once.
/// </para>
/// </remarks>
public static class TypeArgumentSites
{
    /// <summary>The generic subjects every site uses; each type parameter the rule reads carries <c>{C}</c>.</summary>
    public const string Subjects =
        """
        global using System;
        global using System.Runtime.CompilerServices;
        global using CodoMetis.TypeKit;

        public sealed class Box<{C} T> { public static int Count => 0; }
        public class Base<{C} T> { }
        public delegate void Handle<{C} T>(T value);

        [AttributeUsage(AttributeTargets.All)]
        public sealed class MarkAttribute<{C} T> : Attribute { }

        public static class Runner
        {
            public static void Run<{C} T>(T value) { }
        }

        public sealed class Holder
        {
            public void Inst<{C} T>() { }
            public void Take<{C} T>(T value) { }
        }

        public static class Classic
        {
            public static void Ext<{C} T>(this T value) { }
        }

        public sealed class Crate<T> { }

        public static class Blocks
        {
            extension<{C} T>(T value)
            {
                public void Go() { }
                public int Prop => 1;
                public static void Make() { }
            }

            extension<{C} T>(Crate<T> crate)
            {
                public void Open() { }
                public static Crate<T> operator +(Crate<T> left, int right) => left;
            }
        }

        public interface ISender { void Send<{C} T>(T value); }
        public sealed class Sender : ISender { public void Send<T>(T value) { } }
        public class BaseSender { public void Send<T>(T value) { } }
        public sealed class DerivedSender : BaseSender, ISender { }
        public class OpenSender : BaseSender, ISender { }
        public abstract class Overridable { public abstract void Go<{C} T>(); }
        public sealed class Overriding : Overridable { public override void Go<T>() { } }
        public interface IFactory { static abstract void Make<{C} T>(); }
        public sealed class Factory : IFactory { public static void Make<T>() { } }
        public interface IRunner { void Run<{C} T>() { } }
        public interface IFastRunner : IRunner { }

        public sealed class Bag<T> { }
        public struct Cursor<T> { public T Current => default!; public bool MoveNext() => false; }
        public readonly struct Waiter<T> : INotifyCompletion
        {
            public bool IsCompleted => true;
            public T GetResult() => default!;
            public void OnCompleted(Action continuation) { }
        }

        public sealed class Heap<T> : System.Collections.Generic.IEnumerable<T>
        {
            public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static class Patterns
        {
            public static Cursor<T> GetEnumerator<{C} T>(this Bag<T> bag) => default;
            public static Waiter<T> GetAwaiter<{C} T>(this Bag<T> bag) => default;
            public static void Deconstruct<{C} T>(this Bag<T> bag, out int first, out int second) => (first, second) = (0, 0);
            public static void Add<{C} T>(this Heap<T> heap, T item) { }
        }

        """;

    /// <summary>Statements in a method with a value of the argument's type and a receiver for each subject.</summary>
    public static string Body(string statements) =>
        $$"""
          public static class Use
          {
              public static async System.Threading.Tasks.Task Go({A} a, Holder? holder, Bag<{A}> bag, Crate<{A}> crate, object o)
              {
                  {{statements}}
                  await System.Threading.Tasks.Task.CompletedTask;
              }
          }
          """;

    private const string GeneratedHeader = "// <auto-generated/>\n";

    private const string WrittenCall = "public static class Use { public static void Go({A} a) => Runner.Run<{|#0:{A}|}>(a); }";

    private const string InferredCall = "public static class Use { public static void Go({A} a) => {|#0:Runner.Run(a)|}; }";

    public static IReadOnlyList<TypeArgumentSite> All { get; } =
    [
        // Written in a generic name.
        new("a field's type", Reach.Use, "public static class Use { public static Box<{|#0:{A}|}>? Field; }"),
        new("a parameter's type", Reach.Use, "public static class Use { public static void Go(Box<{|#0:{A}|}> box) { } }"),
        new("a return type", Reach.Use, "public static class Use { public static Box<{|#0:{A}|}>? Go() => null; }"),
        new("a local's type", Reach.Use, Body("Box<{|#0:{A}|}>? local = null;")),
        new("an object creation", Reach.Use, Body("_ = new Box<{|#0:{A}|}>();")),
        new("a closed typeof", Reach.Use, Body("_ = typeof(Box<{|#0:{A}|}>);")),
        new("a static member of the closed type", Reach.Use, Body("_ = Box<{|#0:{A}|}>.Count;")),
        new("a type pattern", Reach.Use, Body("_ = o is Box<{|#0:{A}|}>;")),
        new("a cast", Reach.Use, Body("_ = (Box<{|#0:{A}|}>?)o;")),
        new("a default expression", Reach.Use, Body("_ = default(Box<{|#0:{A}|}>);")),
        new("an array of the closed type", Reach.Use, Body("Box<{|#0:{A}|}>[] array = [];")),
        new("a nested type argument", Reach.Use, Body("System.Collections.Generic.List<Box<{|#0:{A}|}>>? list = null;")),
        new("a tuple element", Reach.Use, Body("(Box<{|#0:{A}|}>?, int) pair = (null, 1);")),
        new("a generic delegate", Reach.Use, Body("Handle<{|#0:{A}|}>? handle = null;")),
        new("a base type", Reach.Use, "public class Use : Base<{|#0:{A}|}> { }"),
        new("a constraint clause", Reach.Use, "public static class Use { public static void Go<TBox>() where TBox : Base<{|#0:{A}|}> { } }"),
        new("a using alias", Reach.Use, "using Alias = Box<{|#0:{A}|}>;\npublic static class Use { public static Alias? Field; }"),
        new("a generic attribute", Reach.Use, "[Mark<{|#0:{A}|}>] public sealed class Use { }"),
        new("a generic attribute named in full", Reach.Use, "[MarkAttribute<{|#0:{A}|}>] public sealed class Use { }"),

        // A method reached by name.
        new("a call with written arguments", Reach.Use, Body("Runner.Run<{|#0:{A}|}>(a);")),
        new("a call with inferred arguments", Reach.Use, Body("{|#0:Runner.Run(a)|};")),
        new("a method group, inferred", Reach.Use, Body("Action<{A}> group = {|#0:Runner.Run|};")),
        new("a method group, written", Reach.Use, Body("Action<{A}> group = Runner.Run<{|#0:{A}|}>;")),
        new("a method group as an argument", Reach.Use, Body("Hand({|#0:Runner.Run|});\nstatic void Hand(Action<{A}> handler) { }")),
        new("a method group cast", Reach.Use, Body("_ = (Action<{A}>){|#0:Runner.Run|};")),
        new("a generic local function, inferred", Reach.Use, Body("{|#0:Local(a)|};\nstatic void Local<{C} T>(T value) { }")),
        new("a call in a lambda", Reach.Use, Body("Action act = () => Runner.Run<{|#0:{A}|}>(a);")),
        new("a null-conditional call, written", Reach.Use, Body("holder?.Inst<{|#0:{A}|}>();")),
        new("a null-conditional call, inferred", Reach.Use, Body("holder?{|#0:.Take(a)|};")),
        new("a classic extension method", Reach.Use, Body("{|#0:a.Ext()|};")),
        new("a classic extension method called statically", Reach.Use, Body("{|#0:Classic.Ext(a)|};")),

        // A C# 14 extension block's type parameter, inferred from the receiver.
        new("an extension block's method", Reach.Use, Body("{|#0:a.Go()|};")),
        new("an extension block's property", Reach.Use, Body("_ = {|#0:a.Prop|};")),
        new("an extension block's static member", Reach.Use, Body("{|#0:{A}.Make()|};")),
        new("an extension block's method group", Reach.Use, Body("Action group = {|#0:crate.Open|};")),
        new("an extension block's operator", Reach.Use, Body("_ = {|#0:crate + 1|};")),
        new("an extension block's member, lowered", Reach.Use, Body("{|#0:Blocks.Go(a)|};")),

        // A constraint declared on another member than the one called.
        new("an interface method's implementation", Reach.Use, Body("new Sender().Send<{|#0:{A}|}>(a);")),
        new("an override", Reach.Use, Body("new Overriding().Go<{|#0:{A}|}>();")),
        new("a base class's method implementing an interface for a derived class", Reach.Use, Body("new DerivedSender().Send<{|#0:{A}|}>(a);")),
        new("a base class's implementation called unqualified in the derived class", Reach.Use, "public sealed class Use : BaseSender, ISender { public void Go({A} a) => Send<{|#0:{A}|}>(a); }"),
        new("a base class's implementation called through base.", Reach.Use, "public sealed class Use : BaseSender, ISender { public void Go({A} a) => base.Send<{|#0:{A}|}>(a); }"),
        new("a base class's implementation through a constrained type parameter", Reach.Use, "public static class Use { public static void Go<TSender>(TSender sender, {A} a) where TSender : OpenSender => sender.Send<{|#0:{A}|}>(a); }"),
        new("a static method implementing a static abstract one", Reach.Use, Body("Factory.Make<{|#0:{A}|}>();")),
        new("a static abstract method through a type parameter", Reach.Use, "public static class Use { public static void Go<TFactory>() where TFactory : IFactory => TFactory.Make<{|#0:{A}|}>(); }"),
        new("a default interface method through a derived interface", Reach.Use, "public static class Use { public static void Go(IFastRunner runner) => runner.Run<{|#0:{A}|}>(); }"),

        // A method the compiler binds by pattern; nothing names it.
        new("a foreach's GetEnumerator", Reach.Use, Body("foreach (var item in {|#0:bag|}) { }")),
        new("an await's GetAwaiter", Reach.Use, Body("_ = {|#0:await bag|};")),
        new("a deconstruction", Reach.Use, Body("{|#0:var (p, q) = bag|};")),
        new("a positional pattern", Reach.Use, Body("_ = bag is {|#0:(1, 2)|};")),
        new("a collection expression's Add", Reach.Use, Body("Heap<{A}> heap = {|#0:[a]|};")),

        // Named, not used.
        new("a cref to the generic type", Reach.Mention, "/// <summary>See <see cref=\"Box{T}\"/> and <see cref=\"Base{T}\"/>.</summary>\npublic static class Use { }"),
        new("a cref to the generic method", Reach.Mention, "/// <summary>See <see cref=\"Runner.Run{T}(T)\"/>.</summary>\npublic static class Use { }"),
        new("a nameof of the closed type", Reach.Mention, Body("_ = nameof(Box<{A}>);")),
        new("a nameof of a member of the closed type", Reach.Mention, Body("_ = nameof(Box<{A}>.Count);")),
        new("an unbound typeof", Reach.Mention, Body("_ = typeof(Box<>);")),
        new("an argument that does not exist", Reach.Mention, "public static class Use { public static Box<{|CS0246:Missing|}>? Field; }"),

        // Generated code: reported only where it maps to markup.
        new("generated: an auto-generated header, written", Reach.Hidden, GeneratedHeader + WrittenCall),
        new("generated: an auto-generated header, inferred", Reach.Hidden, GeneratedHeader + InferredCall),
        new("generated: a .g.cs file", Reach.Hidden, WrittenCall, "Use.g.cs"),
        new("generated: under #line hidden", Reach.Hidden, "#line hidden\n" + WrittenCall + "\n#line default"),
        new("generated: mapped to Razor markup", Reach.Use, GeneratedHeader + "#line 7 \"Component.razor\"\n" + WrittenCall + "\n#line default", "Component.razor.g.cs"),
    ];
}

/// <summary>
/// What a rule supplies to run the catalogue: the constraint it reads, an argument that breaks it and
/// one that meets it, the declarations those need, and the sites where the rule, by a decision, is
/// silent, each with the decision.
/// </summary>
public sealed record TypeArgumentSubject(
    string Name,
    string Constraint,
    string Breaking,
    string Meeting,
    string Declarations,
    IReadOnlyDictionary<string, string>? SilentAt = null)
{
    public override string ToString() => Name;
}
