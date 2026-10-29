using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0003 over <see cref="DroppedTaskShapes"/>: a call's task dropped in every form and through every
/// pass-through is reported, once per call that made it, and kept in any other use is silent.
/// </summary>
/// <remarks>
/// The oracle is the rule's own statement, which each shape holds to: the statement's own call is reported
/// whole, a branch's call at the branch, and an awaited task only where what it completes with is a result.
/// An assertion of CodoMetis.TypeKit.Testing and a call that returns a task of a <c>Result</c> run through
/// the same shapes, so the two forms of the rule agree on each (#152).
/// </remarks>
public sealed class DroppedTaskVariantTests
{
    public static TheoryData<string> Calls() => [.. DroppedTaskShapes.Calls.Select(call => call.Name)];

    [Theory]
    [MemberData(nameof(Calls))]
    public Task A_dropped_task_is_reported_in_every_form_and_a_kept_one_never(string name)
    {
        var call = DroppedTaskShapes.Calls.Single(candidate => candidate.Name == name);
        var (source, expected) = DroppedTaskShapes.Use(call);

        var test = new AnalyzerTest<IgnoredOutcomeAnalyzer>(source);
        test.TestState.Sources.Add(("Declarations.cs", DroppedTaskShapes.Declarations));
        test.TestState.AdditionalReferences.Add(RealTesting.Reference);
        test.ExpectedDiagnostics.AddRange(
            Enumerable.Range(0, expected).Select(location =>
                new DiagnosticResult("CMTK0003", DiagnosticSeverity.Error).WithLocation(location).WithArguments(call.Outcome, call.Method)));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Every axis of the catalogue reaches the sweep: a shape that compiles to no case would pass as silent.</summary>
    [Fact]
    public void Every_shape_makes_a_case()
    {
        foreach (var call in DroppedTaskShapes.Calls)
        {
            var (source, expected) = DroppedTaskShapes.Use(call);

            foreach (var passThrough in DroppedTaskShapes.PassThroughs.Where(passThrough => passThrough.Suffix.Length > 0))
                source.ShouldContain(passThrough.Suffix, customMessage: $"{call}: no case passes through {passThrough}.");

            foreach (var form in DroppedTaskShapes.Forms.Where(form => DroppedTaskShapes.Applies(form, call)))
                source.ShouldContain($"// {form.Name}, ", customMessage: $"{call}: no case of {form}.");

            expected.ShouldBeGreaterThan(0, $"{call}: nothing is expected to be reported.");
        }
    }
}
