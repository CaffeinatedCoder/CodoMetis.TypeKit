using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0003: a <c>Result</c> or <c>Option</c> dropped by an expression statement.</summary>
public sealed class IgnoredOutcomeAnalyzerTests
{
    private const string Subjects =
        """
        using System.Threading.Tasks;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public enum Fault { Refused }

        public readonly struct Validated : IValidatedValue<Validated, int, Fault>
        {
            public static Result<Validated, Fault> Create(int value) => Result<Validated, Fault>.Error(Fault.Refused);
        }

        public sealed class Service
        {
            public Result<int, Fault> Load() => 1;

            public Result<Fault> Save(int value) => Result.Success();

            public Option<int> Find() => Option.Some(1);

            public Task<Result<int, Fault>> LoadAsync() => Task.FromResult(Load());

            public ValueTask<Result<Fault>> SaveAsync() => new(Save(1));

            public int Count() => 1;
        }

        """;

    private static AnalyzerTest<IgnoredOutcomeAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<IgnoredOutcomeAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0003(int location, string outcome, string method) =>
        new DiagnosticResult("CMTK0003", DiagnosticSeverity.Warning).WithLocation(location).WithArguments(outcome, method);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new IgnoredOutcomeAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0003");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Warning);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task An_ignored_result_reports() =>
        Test(
            """
            public static class Caller
            {
                public static void Run(Service service) { {|#0:service.Save(1)|}; }
            }
            """,
            new DiagnosticResult("CMTK0003", DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithMessage("The Result that 'Save' returns is ignored, so its outcome is never seen. Handle it, or discard it with '_ =' if nothing depends on it."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task Every_form_of_dropping_one_reports() =>
        Test(
            """
            public static class Caller
            {
                public static async Task Run(Service service, Service? maybe)
                {
                    {|#0:service.Load()|};
                    {|#1:service.Find()|};
                    {|#2:await service.LoadAsync()|};
                    {|#3:await service.SaveAsync()|};
                    {|#4:service.LoadAsync()|};
                    {|#5:service.Load().Map(x => x + 1)|};
                    {|#6:maybe?.Load()|};
                    {|#7:Validated.Create(1)|};
                    {|#8:await service.LoadAsync().ConfigureAwait(false)|};
                }
            }
            """,
            Cmtk0003(0, "Result", "Load"),
            Cmtk0003(1, "Option", "Find"),
            Cmtk0003(2, "Result", "LoadAsync"),
            Cmtk0003(3, "Result", "SaveAsync"),
            Cmtk0003(4, "Result", "LoadAsync"),
            Cmtk0003(5, "Result", "Map"),
            Cmtk0003(6, "Result", "Load"),
            Cmtk0003(7, "Result", "Create"),
            Cmtk0003(8, "Result", "LoadAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary><c>Tap</c> returns its receiver: dropping it loses nothing only when the receiver is still held.</summary>
    [Fact]
    public Task Tap_on_a_stored_result_is_silent_and_on_a_call_reports() =>
        Test(
            """
            public static class Caller
            {
                private static Result<int, Fault> _field = 1;

                public static async Task Run(Service service, Result<int, Fault> parameter)
                {
                    var local = service.Load();
                    local.Tap(_ => { });
                    parameter.Tap(_ => { });
                    _field.Tap(_ => { });
                    await local.TapAsync(_ => Task.CompletedTask);

                    {|#0:service.Load().Tap(_ => { })|};
                    {|#1:await service.LoadAsync().TapAsync(_ => { })|};
                }
            }
            """,
            Cmtk0003(0, "Result", "Tap"),
            Cmtk0003(1, "Result", "TapAsync"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// A generated <c>TryFrom</c> on a value object of the same project does not bind where Metalama
    /// runs analyzers; CS0117 here reproduces that compilation.
    /// </summary>
    [Fact]
    public Task An_ignored_TryFrom_that_does_not_bind_reports() =>
        Test(
            """
            public static class Caller
            {
                public static void Run() { {|#0:Validated.{|CS0117:TryFrom|}(1)|}; }
            }
            """,
            Cmtk0003(0, "Option", "TryFrom"))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_used_or_explicitly_discarded_result_stays_silent() =>
        Test(
            """
            public static class Caller
            {
                public static Result<int, Fault> Run(Service service)
                {
                    _ = service.Load();
                    var kept = service.Find();
                    service.Save(service.Load().Match(x => x, _ => 0)).TryGetError(out _);
                    service.Count();
                    Result<int, Fault> assigned;
                    assigned = service.Load();
                    return service.Load();
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<IgnoredOutcomeAnalyzer>("public static class S { public static int F() => 1; public static void G() { F(); } }", referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
