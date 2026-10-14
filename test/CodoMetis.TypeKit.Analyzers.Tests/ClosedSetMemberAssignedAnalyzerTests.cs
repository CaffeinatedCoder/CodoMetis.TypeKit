using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0013: a closed set's member written outside its declaration, after the generators named the
/// instance it held, or before, giving one instance two names.
/// </summary>
/// <remarks>
/// The verifier never weaves, so the set declares by hand what the generators would. BuildOutcomeTests
/// holds the rule to a real woven build.
/// </remarks>
public sealed class ClosedSetMemberAssignedAnalyzerTests
{
    private static string Set(string body) =>
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using CodoMetis.TypeKit.ClosedSets;

        public sealed partial class Status : IClosedSet<Status>
        {
            public static readonly Status Open = new();
            public static readonly Status Closed = new();

            private Status() { }

            public string Name => "";
            public string QualifiedName => "";
            public static IReadOnlyList<Status> All => [Open, Closed];
            public static bool TryParse([NotNullWhen(true)] string? name, [MaybeNullWhen(false)] out Status member) { member = null; return false; }

            private static void Make(out Status member) => member = new();
            private static void Read(in Status member) { }
            private static void Take(Status member) { }

            private ref struct Holder
            {
                public ref readonly Status Member;
                public ref Status Writable;
            }

        {{body}}
        }
        """;

    private static AnalyzerTest<ClosedSetMemberAssignedAnalyzer> Test(string source, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<ClosedSetMemberAssignedAnalyzer>(source);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0013(int location, string member) =>
        new DiagnosticResult("CMTK0013", DiagnosticSeverity.Error).WithLocation(location).WithArguments("Status", member);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new ClosedSetMemberAssignedAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0013");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    /// <summary>The defect as found: Closed.Match took the open branch, and Parse("Open") wrote "Closed".</summary>
    [Fact]
    public Task An_assignment_in_the_static_constructor_is_reported() =>
        Test(Set("    static Status() { {|#0:Open|} = Closed; }"), Cmtk0013(0, "Open"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Every form of a write C# allows a static readonly field there, each reported at the field.</summary>
    [Fact]
    public Task Every_form_of_a_write_is_reported() =>
        Test(
            Set(
                """
                    static Status()
                    {
                        {|#0:Status.Open|} = Closed;
                        {|#1:Open|} ??= Closed;
                        ({|#2:Open|}, ({|#3:Closed|}, _)) = (Closed, (Open, 0));
                        Make(out {|#4:Closed|});
                        Interlocked.Exchange(ref {|#5:Open|}, Closed);
                        ref Status alias = ref {|#6:Closed|};
                        alias = Open;
                        alias = ref {|#7:Open|};
                        (Open.Name.Length == 0 ? ref {|#8:Open|} : ref {|#9:Closed|}) = Closed;
                        Make(out (Open.Name.Length == 0 ? ref {|#10:Open|} : ref {|#11:Closed|}));
                        ref Status either = ref Open.Name.Length == 0 ? ref {|#12:Open|} : ref {|#13:Closed|};
                        ref readonly Status read = ref Open.Name.Length == 0 ? ref Open : ref Closed;
                        var holder = new Holder();
                        holder.Writable = ref {|#14:Open|};
                    }
                """),
            Cmtk0013(0, "Open"), Cmtk0013(1, "Open"), Cmtk0013(2, "Open"), Cmtk0013(3, "Closed"),
            Cmtk0013(4, "Closed"), Cmtk0013(5, "Open"), Cmtk0013(6, "Closed"), Cmtk0013(7, "Open"),
            Cmtk0013(8, "Open"), Cmtk0013(9, "Closed"), Cmtk0013(10, "Open"), Cmtk0013(11, "Closed"), Cmtk0013(12, "Open"), Cmtk0013(13, "Closed"),
            Cmtk0013(14, "Open"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A static field's initializer runs before the names are given, so a member assigned there gives
    /// the instance two names, the later winning.
    /// </summary>
    [Fact]
    public Task An_assignment_in_another_field_s_initializer_is_reported() =>
        Test(Set("    private static readonly bool Swapped = ({|#0:Open|} = Closed) is not null;"), Cmtk0013(0, "Open"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The static constructor may be in another part of the set.</summary>
    [Fact]
    public Task An_assignment_in_another_part_is_reported() =>
        new AnalyzerTest<ClosedSetMemberAssignedAnalyzer>(Set(""))
        {
            TestState = { Sources = { ("Other.cs", "public sealed partial class Status { static Status() { {|#0:Closed|} = Open; } }") } },
            ExpectedDiagnostics = { Cmtk0013(0, "Closed") },
        }
        .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Reading the members after their names are given is what a static constructor is for: a lookup,
    /// an argument by value or by <c>in</c>, a <c>ref readonly</c> local or field, declared or
    /// reassigned, a comparison.
    /// </summary>
    [Fact]
    public Task Reads_are_silent() =>
        Test(
            Set(
                """
                    private static readonly Dictionary<Status, int> Order;

                    static Status()
                    {
                        Order = new() { [Open] = 0, [Closed] = 1 };
                        var first = Open;
                        Take(Closed);
                        Read(in Open);
                        ref readonly Status alias = ref Closed;
                        alias = ref Open;
                        alias = ref Open.Name.Length == 0 ? ref Open : ref Closed;
                        var holder = new Holder();
                        holder.Member = ref Closed;
                        _ = ReferenceEquals(Open, Closed) || Open.Name.Length == 0;
                    }
                """))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>The same writes in a class that is no closed set, and to a static field of the set that is of another type, are nobody's concern here.</summary>
    [Fact]
    public Task A_class_that_is_no_closed_set_and_a_field_of_another_type_are_silent() =>
        Test(
            Set(
                """
                    private static readonly string Label = "";

                    static Status() { Label = "set"; }
                }

                public sealed class Plain
                {
                    public static readonly Plain Open = new();
                    public static readonly Plain Closed = new();

                    static Plain() { Open = Closed; }
                """))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>Robustness, not a guard: without CodoMetis.TypeKit there is nothing to look at, and nothing may fail.</summary>
    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<ClosedSetMemberAssignedAnalyzer>("public sealed class S { public static readonly S A = new(); static S() { A = new(); } }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
