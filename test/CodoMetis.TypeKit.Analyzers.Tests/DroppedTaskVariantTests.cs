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
/// whole, a branch's call at the branch, a combination of assertions at the combinator, and an awaited task
/// only where what it completes with is a result, or a <c>WhenAny</c> that leaves every failure unobserved.
/// An assertion of CodoMetis.TypeKit.Testing and a call that returns a task of a <c>Result</c> run through
/// the same shapes, so the two forms of the rule agree on each (#152).
/// </remarks>
public sealed class DroppedTaskVariantTests
{
    public static TheoryData<string, string> Cases()
    {
        var cases = new TheoryData<string, string>();
        foreach (var call in DroppedTaskShapes.Calls)
        foreach (var form in DroppedTaskShapes.Forms.Where(form => DroppedTaskShapes.Applies(form, call)))
            cases.Add(call.Name, form.Name);

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task A_dropped_task_is_reported_in_every_form_and_a_kept_one_never(string name, string shape)
    {
        var call = DroppedTaskShapes.Calls.Single(candidate => candidate.Name == name);
        var form = DroppedTaskShapes.Forms.Single(candidate => candidate.Name == shape);
        var (source, expected) = DroppedTaskShapes.Use(call, form);

        var test = new AnalyzerTest<IgnoredOutcomeAnalyzer>(source);
        test.TestBehaviors = VariantSweep.Behaviors;
        test.TestState.Sources.Add(("Declarations.cs", DroppedTaskShapes.Declarations));
        test.TestState.AdditionalReferences.Add(RealTesting.Reference);
        test.ExpectedDiagnostics.AddRange(
            expected.Select((message, location) =>
                new DiagnosticResult("CMTK0003", DiagnosticSeverity.Error).WithLocation(location).WithArguments(message.Outcome, message.Method)));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every axis of the catalogue reaches the sweep, and each call has a form that reports and one that keeps:
    /// a shape that compiles to no case would pass as silent.
    /// </summary>
    [Fact]
    public void Every_shape_makes_a_case()
    {
        foreach (var call in DroppedTaskShapes.Calls)
        {
            var uses = DroppedTaskShapes.Forms.Where(form => DroppedTaskShapes.Applies(form, call)).Select(form => DroppedTaskShapes.Use(call, form)).ToList();
            var source = string.Concat(uses.Select(use => use.Source));

            foreach (var passThrough in DroppedTaskShapes.PassThroughs.Where(passThrough => passThrough.Suffix.Length > 0))
                source.ShouldContain(passThrough.Suffix, customMessage: $"{call}: no case passes through {passThrough}.");

            foreach (var use in DroppedTaskShapes.Uses)
                source.ShouldContain($", {use.Name}\n", customMessage: $"{call}: no case is {use}.");

            uses.ShouldContain(use => use.Expected.Count > 0, $"{call}: nothing is expected to be reported.");
            uses.Where(use => use.Source.Contains("{|#", StringComparison.Ordinal)).Count().ShouldBeLessThan(uses.Count, $"{call}: every form reports, so no form keeps the task.");
        }
    }
}
