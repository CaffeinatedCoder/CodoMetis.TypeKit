using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// A verifier run of one analyzer over a test source that binds to the real CodoMetis.TypeKit
/// assembly.
/// </summary>
/// <remarks>
/// The test sources declare none of the contracts themselves. The analyzer resolves them by metadata
/// name, so a copy declared in the test would keep the tests green through a rename of the real
/// types while the analyzer went silent in every consumer. Against the real assembly, such a drift
/// silences the positive tests instead, and they fail.
/// </remarks>
internal sealed class AnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public AnalyzerTest(string source, bool referenceTypeKit = true)
    {
        TestCode            = source;
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100;

        if (referenceTypeKit)
            TestState.AdditionalReferences.Add(RealTypeKit.Reference);
    }
}

/// <summary>What a sweep over a variant catalogue leaves to the focused tests of its rule.</summary>
/// <remarks>
/// The verifier runs a case that expects a diagnostic a second time, under <c>#pragma warning disable</c> for each id
/// it expects, and checks that nothing is left. Whether a pragma silences a rule is the compiler's, the same for every
/// shape, so a sweep paid for that pass once per row: 28% of DroppedTaskVariantTests' time (#245). The focused tests
/// of each rule keep it. The verifier's other pass, under an <c>&lt;auto-generated&gt;</c> header, never runs here:
/// every analyzer configures generated-code analysis (<c>GeneratedCode.AnalysisFlags</c>), and GeneratedCodeTests
/// holds what each rule does in generated code.
/// </remarks>
internal static class VariantSweep
{
    public const TestBehaviors Behaviors = TestBehaviors.SkipSuppressionCheck;
}

/// <summary>A verifier run of one analyzer and its code fix: the source, then the source as the fix leaves it.</summary>
internal sealed class CodeFixTest<TAnalyzer, TCodeFix> : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public CodeFixTest(string source, string fixedSource)
    {
        TestCode            = source;
        FixedCode           = fixedSource;
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100;

        TestState.AdditionalReferences.Add(RealTypeKit.Reference);
        FixedState.AdditionalReferences.Add(RealTypeKit.Reference);
    }
}

/// <summary>The real EF Core satellite and the EF Core assembly its converter derives from.</summary>
internal static class RealEntityFrameworkCore
{
    public static MetadataReference[] References { get; } =
    [
        MetadataReference.CreateFromFile(typeof(global::CodoMetis.TypeKit.EntityFrameworkCore.ValueObjectConverter<,>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter).Assembly.Location),
    ];
}

/// <summary>The real test assertions, whose assertions on a pending value CMTK0003 knows by the assembly's identity.</summary>
internal static class RealTesting
{
    public static MetadataReference Reference { get; } =
        MetadataReference.CreateFromFile(typeof(global::CodoMetis.TypeKit.Testing.TypeKitAssertionException).Assembly.Location);
}

internal static class RealTypeKit
{
    public static Assembly Assembly { get; } = typeof(Option<>).Assembly;

    public static MetadataReference Reference { get; } = MetadataReference.CreateFromFile(Assembly.Location);

    /// <summary>An empty assembly with the given identity, to stand in for a referenced package.</summary>
    public static MetadataReference EmptyAssemblyNamed(string name)
    {
        var compilation = CSharpCompilation.Create(
            name,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);

        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
