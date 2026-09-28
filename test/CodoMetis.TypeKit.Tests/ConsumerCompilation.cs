using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// Compiles a consumer's source against the base assembly under test, as C# 14 with nullable
/// annotations on, so a test can ask what a call binds to, or whether a piece of advice compiles.
/// </summary>
internal static class ConsumerCompilation
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                                                                       .Append(typeof(Option<>).Assembly.Location)
                                                                       .Distinct(StringComparer.Ordinal)
                                                                       .Select(path => MetadataReference.CreateFromFile(path)),
    ]);

    public static CSharpCompilation Of(string source) =>
        CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>The errors and warnings, one per line, or empty when it compiles cleanly.</summary>
    public static string Problems(this Compilation compilation) =>
        string.Join(Environment.NewLine, compilation.GetDiagnostics()
                                                    .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
                                                    .Select(diagnostic => diagnostic.ToString()));

    /// <summary>The ids of the errors.</summary>
    public static IReadOnlyList<string> ErrorIds(this Compilation compilation) =>
        [.. compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id)];

    /// <summary>The one method the source declares, and the method its one invocation binds to.</summary>
    public static (IMethodSymbol Declared, IMethodSymbol Called) TheOnlyCall(this Compilation compilation)
    {
        var tree  = compilation.SyntaxTrees.Single();
        var root  = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);

        var declared = model.GetDeclaredSymbol(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single())
                    ?? throw new InvalidOperationException("The source declares no method.");
        var called = model.GetSymbolInfo(root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single()).Symbol as IMethodSymbol
                  ?? throw new InvalidOperationException($"The call binds to no method: {compilation.Problems()}");

        return (declared, called);
    }
}
