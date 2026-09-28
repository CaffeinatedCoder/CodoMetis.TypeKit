using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>CMTK0007: <c>FromKnownGood</c> given a value that arrives from the caller.</summary>
public sealed class KnownGoodFromCallerAnalyzerTests
{
    /// <summary>
    /// <c>Validated</c> declares <c>FromKnownGood</c> by hand, as the generators allow, with the
    /// generated one's shape. <c>Unwoven</c> has none, which is how a value object of the same project
    /// looks where Metalama runs analyzers.
    /// </summary>
    private const string Subjects =
        """
        using System.Linq;
        using System.Runtime.CompilerServices;
        using CodoMetis.TypeKit;
        using CodoMetis.TypeKit.ValueObjects;

        public enum Fault { Refused }

        public readonly struct Validated : IValidatedValue<Validated, string, Fault>
        {
            public static Result<Validated, Fault> Create(string value) => Result<Validated, Fault>.Error(Fault.Refused);

            public static Validated FromKnownGood(string value, [CallerArgumentExpression(nameof(value))] string? expression = null) => default;
        }

        public readonly struct Unwoven : IValidatedValue<Unwoven, string, Fault>
        {
            public static Result<Unwoven, Fault> Create(string value) => Result<Unwoven, Fault>.Error(Fault.Refused);
        }

        public readonly struct Plain : IValue<string>
        {
            public static Plain FromKnownGood(string value) => default;
        }

        public sealed record Request(string Email, string[] Codes);

        """;

    private static AnalyzerTest<KnownGoodFromCallerAnalyzer> Test(string code, params DiagnosticResult[] expected)
    {
        var test = new AnalyzerTest<KnownGoodFromCallerAnalyzer>(Subjects + code);
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static DiagnosticResult Cmtk0007(int location, string valueObject, string value) =>
        new DiagnosticResult("CMTK0007", DiagnosticSeverity.Info).WithLocation(location).WithArguments(valueObject, value);

    [Fact]
    public void The_descriptor_is_the_published_contract()
    {
        var rule = new KnownGoodFromCallerAnalyzer().SupportedDiagnostics.ShouldHaveSingleItem();

        rule.Id.ShouldBe("CMTK0007");
        rule.DefaultSeverity.ShouldBe(DiagnosticSeverity.Info);
        rule.IsEnabledByDefault.ShouldBeTrue();
    }

    [Fact]
    public Task A_parameter_reports() =>
        Test(
            "public static class C { public static Validated Of(string input) => Validated.FromKnownGood({|#0:input|}); }",
            new DiagnosticResult("CMTK0007", DiagnosticSeverity.Info)
                .WithLocation(0)
                .WithMessage("'Validated.FromKnownGood' is given 'input', which arrives from the caller, and throws if it breaks the rules. Validate input with Create or TryFrom; FromKnownGood is for values known to be valid."))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_member_or_element_of_a_parameter_a_lambda_parameter_and_an_unbound_call_report() =>
        Test(
            """
            public static class C
            {
                public static Validated Member(Request request) => Validated.FromKnownGood({|#0:request.Email|});

                public static Validated Element(string[] args) => Validated.FromKnownGood({|#1:args[0]|});

                public static Validated[] Lambda(Request request) => request.Codes.Select(code => Validated.FromKnownGood({|#2:code|})).ToArray();

                public static Unwoven Unbound(string input) => Unwoven.{|CS0117:FromKnownGood|}({|#3:input|});
            }
            """,
            Cmtk0007(0, "Validated", "request.Email"),
            Cmtk0007(1, "Validated", "args[0]"),
            Cmtk0007(2, "Validated", "code"),
            Cmtk0007(3, "Unwoven", "input"))
            .RunAsync(TestContext.Current.CancellationToken);

    /// <summary>What the code produced itself, and a <c>FromKnownGood</c> that is not a validated value object's.</summary>
    [Fact]
    public Task A_value_the_code_produced_stays_silent() =>
        Test(
            """
            public static class C
            {
                private const string Known = "ABC";
                private static readonly string Configured = "ABC";

                public static void Run(string input)
                {
                    Validated.FromKnownGood("ABC");
                    Validated.FromKnownGood(Known);
                    Validated.FromKnownGood(Configured);
                    Validated.FromKnownGood(input.ToUpperInvariant());
                    Validated.FromKnownGood($"{input}-1");
                    var local = input.Trim();
                    Validated.FromKnownGood(local);
                    Plain.FromKnownGood(input);
                }
            }
            """)
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public Task A_compilation_without_TypeKit_is_left_alone() =>
        new AnalyzerTest<KnownGoodFromCallerAnalyzer>(
                "public static class S { public static int FromKnownGood(int v) => v; public static int Use(int p) => FromKnownGood(p); }",
                referenceTypeKit: false)
            .RunAsync(TestContext.Current.CancellationToken);
}
