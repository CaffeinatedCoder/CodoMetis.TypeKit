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
