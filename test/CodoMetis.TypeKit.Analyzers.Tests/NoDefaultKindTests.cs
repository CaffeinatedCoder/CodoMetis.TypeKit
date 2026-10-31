using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// CMTK0001, CMTK0005 and CMTK0006 across the variant catalogue of what a type is (<see cref="TypeArgumentKinds"/>):
/// each reports a kind exactly when the others do.
/// </summary>
/// <remarks>
/// The Analyzers README defines CMTK0005 and CMTK0006 over "such a type", CMTK0001's list, which takes in a type
/// parameter whose constraints make it a value object. CMTK0005 and CMTK0006 read a narrower predicate of their own
/// and were silent for <c>where T : IValue&lt;int&gt;, new()</c>, which CMTK0001 reports (#207). The oracle is that
/// list: a struct value object, or a parameter bound by the marker beside <c>struct</c>, <c>unmanaged</c> or
/// <c>new()</c> and no class bound, since a generated class has no public parameterless constructor.
/// </remarks>
public sealed class NoDefaultKindTests
{
    internal static bool HasNoDefault(TypeArgumentKind kind) => kind switch
    {
        KindOfType type           => type.Is.HasFlag(Is.Struct) && type.Is.HasFlag(Is.ValueObjectMarker),
        KindOfParameter parameter => parameter.Facts.Declares.HasFlag(Declares.ValueObjectBound)
                                  && (parameter.Facts.Declares & (Declares.Struct | Declares.Unmanaged | Declares.New)) != 0
                                  && (parameter.Facts.Declares & (Declares.Class | Declares.ClassBound)) == 0,
        _                         => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static TheoryData<string> Kinds() => [.. TypeArgumentKinds.All.Where(kind => kind.Mentions.All(Declared)).Select(kind => kind.Name)];

    private static bool Declared(string attribute) => RealTypeKit.Assembly.GetType($"CodoMetis.TypeKit.{attribute}Attribute") is not null;

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task CMTK0001_reports_a_default_of_the_kind_as_the_list_says(string kindName) =>
        Run<ForbiddenDefaultInitializationAnalyzer>(kindName, "CMTK0001", DiagnosticSeverity.Error,
            (kind, at) => $$"""public static class Use{{kind.Parameters}} {{kind.Where}} { public static {{kind.Argument}} M() => {{at("default(" + kind.Argument + ")")}}; }""");

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task CMTK0005_reports_an_array_of_the_kind_as_CMTK0001_does(string kindName) =>
        Run<DefaultFilledCollectionAnalyzer>(kindName, "CMTK0005", DiagnosticSeverity.Warning,
            (kind, at) => $$"""public static class Use{{kind.Parameters}} {{kind.Where}} { public static {{kind.Argument}}[] M(int n) => {{at($"GC.AllocateArray<{kind.Argument}>(n)")}}; }""");

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task CMTK0006_reports_an_unassigned_field_of_the_kind_as_CMTK0001_does(string kindName) =>
        Run<UnassignedNoDefaultMemberAnalyzer>(kindName, "CMTK0006", DiagnosticSeverity.Warning,
            (kind, at) => $$"""public sealed class Holder{{kind.Parameters}} {{kind.Where}} { public {{kind.Argument}} {{at("Field")}}; }""");

    private static Task Run<TAnalyzer>(string kindName, string id, DiagnosticSeverity severity, Func<TypeArgumentKind, Func<string, string>, string> use)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var kind      = TypeArgumentKinds.All.Single(candidate => candidate.Name == kindName);
        var reporting = HasNoDefault(kind);
        var test      = new AnalyzerTest<TAnalyzer>("#nullable enable\n" + use(kind, code => reporting ? $"{{|#0:{code}|}}" : code));

        test.TestState.Sources.Add(("Candidates.cs", TypeArgumentKinds.Candidates));
        if (reporting) test.ExpectedDiagnostics.Add(new DiagnosticResult(id, severity).WithLocation(0));

        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
