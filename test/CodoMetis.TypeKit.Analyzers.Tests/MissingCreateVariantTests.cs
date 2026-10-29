using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CodoMetis.TypeKit.Analyzers.Tests.Variants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace CodoMetis.TypeKit.Analyzers.Tests;

/// <summary>
/// The fix for a missing <c>Create</c> across its input class (design/adr/0095): what the wrapped type
/// and the fault are (<see cref="TypeArgumentKinds"/>), and the declaration's shape and where it is
/// nested. The oracle is the compiler's: before the fix the file fails only for the missing member,
/// after it the file compiles and the type's <c>Create</c> implements the contract.
/// </summary>
/// <remarks>
/// <see cref="TypeArgumentSites"/> does not apply: the fix reads a declaration's base list, not a type
/// argument at a use. A kind that is no valid argument there is named below with the compiler's reason,
/// so a kind that stops compiling is a failure, not a skip.
/// </remarks>
public sealed class MissingCreateVariantTests
{
    /// <summary>Kinds the declaration refuses before any fix, and the error it refuses them with.</summary>
    private static readonly ImmutableDictionary<string, string> NotAnArgument = new Dictionary<string, string>
    {
        ["dynamic"] = "CS1966", // an interface's type argument cannot be dynamic
    }.ToImmutableDictionary();

    public static TheoryData<string> Kinds() => new(TypeArgumentKinds.All.Select(kind => kind.Name));

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task The_fix_compiles_whatever_the_wrapped_type_is(string kind) =>
        Sweep(kind, (argument, _) => ("Subject", argument, "Shade"));

    [Theory]
    [MemberData(nameof(Kinds))]
    public Task The_fix_compiles_whatever_the_fault_is(string kind) =>
        Sweep(kind, (argument, _) => ("Subject", "string", argument));

    public static TheoryData<string, string> Shapes() => new()
    {
        { "public readonly partial record struct", "" },
        { "public partial record struct", "" },
        { "public sealed partial record", "" },
        { "public partial record", "" },
        { "public readonly partial struct", "" },
        { "public sealed partial class", "" },
        { "public partial class", "" },
        { "public readonly partial record struct", "class" },
        { "public sealed partial record", "struct" },
        { "public readonly partial record struct", "generic" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task The_fix_compiles_whatever_the_declaration_s_shape(string declaration, string nesting)
    {
        var (open, close, outer) = nesting switch
        {
            "class"   => ("public partial class Outer\n{\n", "}\n", "public partial class Outer"),
            "struct"  => ("public partial struct Outer\n{\n", "}\n", "public partial struct Outer"),
            "generic" => ("public partial class Outer<TItem> where TItem : class\n{\n", "}\n", "public partial class Outer<TItem> where TItem : class"),
            _         => ("", "", null),
        };

        var source    = $"{open}{declaration} Subject : CodoMetis.TypeKit.ValueObjects.IValidatedValue<Subject, string, Shade>;\n{close}";
        var generated = Wrap(outer, MissingCreateCodeFixTests.Constructor($"{declaration} Subject", "string"));

        await Verify(source, generated, nesting == "" ? "Subject" : nesting == "generic" ? "Outer`1+Subject" : "Outer+Subject");
    }

    private static async Task Sweep(string name, Func<string, TypeArgumentKind, (string Self, string Wrapped, string Fault)> shape)
    {
        var kind = TypeArgumentKinds.All.Single(candidate => candidate.Name == name);
        var (self, wrapped, fault) = shape(kind.Argument, kind);

        var holder    = $"public partial class Holder{kind.Parameters} {kind.Where}";
        var source    = $"{holder}\n{{\n    public readonly partial record struct {self} : CodoMetis.TypeKit.ValueObjects.IValidatedValue<{self}, {wrapped}, {fault}>;\n}}\n";
        // An attribute on a type parameter is written once: the parts' attributes merge (CS0579).
        var bare      = $"public partial class Holder{Regex.Replace(kind.Parameters, @"\[[^\]]*\]\s*", "")} {kind.Where}";
        var generated = $"{bare} {{ public readonly partial record struct {self} {{ private {self}({wrapped} value) {{ }} }} }}";
        var metadata  = kind.Parameters.Length == 0 ? "Holder+Subject" : $"Holder`{kind.Parameters.Count(c => c == ',') + 1}+Subject";

        if (NotAnArgument.TryGetValue(kind.Argument, out var refused))
        {
            var before = await CompileAsync(Project(source, generated));
            before.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id).ShouldContain(refused);
            return;
        }

        await Verify(source, generated, metadata);
    }

    private static async Task Verify(string source, string generated, string metadataName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project           = Project(source, generated);
        var before            = await CompileAsync(project);

        var errors = before.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.Select(d => d.Id).ShouldBe(["CS0535"], string.Join(Environment.NewLine, errors));

        var document = project.Documents.Single(d => d.Name == "Subject.cs");
        var actions  = new List<CodeAction>();
        var context  = new CodeFixContext(document, errors[0], (action, _) => actions.Add(action), cancellationToken);

        await new MissingCreateCodeFixProvider().RegisterCodeFixesAsync(context);
        var action = actions.ShouldHaveSingleItem();

        var operations = await action.GetOperationsAsync(cancellationToken);
        var solution   = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var after      = await CompileAsync(solution.GetProject(project.Id)!);
        var text       = (await solution.GetDocument(document.Id)!.GetTextAsync(cancellationToken)).ToString();

        after.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty(text);

        var subject  = after.GetTypeByMetadataName(metadataName).ShouldNotBeNull(text);
        var contract = subject.AllInterfaces.Single(i => i.OriginalDefinition.MetadataName == "IValidatedValue`3");
        var create   = (IMethodSymbol)subject.FindImplementationForInterfaceMember(contract.GetMembers("Create").Single()).ShouldNotBeNull(text);

        create.IsStatic.ShouldBeTrue(text);
        create.DeclaredAccessibility.ShouldBe(Accessibility.Public, text);
    }

    /// <summary>
    /// For each CS0535 of the file, the base type it is reported at and whether the fix is offered there:
    /// one interface can bring two missing members, and another interface a <c>Create</c> of its own.
    /// </summary>
    internal static async Task<List<(string BaseType, bool Offered)>> OfferedAsync(string source, string generated)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project           = Project(source, generated);
        var document          = project.Documents.Single(d => d.Name == "Subject.cs");
        var root              = await document.GetSyntaxRootAsync(cancellationToken);
        var offered           = new List<(string, bool)>();

        foreach (var diagnostic in (await CompileAsync(project)).GetDiagnostics(cancellationToken).Where(d => d.Id == "CS0535"))
        {
            var actions = new List<CodeAction>();
            await new MissingCreateCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken));
            offered.Add((root!.FindNode(diagnostic.Location.SourceSpan).ToString(), actions.Count > 0));
        }

        return offered;
    }

    private static string Wrap(string? outer, string member) => outer is null ? member : $"{outer} {{ {member} }}";

    private static Project Project(string source, string generated)
    {
        var workspace = new AdhocWorkspace();
        var project   = workspace.AddProject("Variants", LanguageNames.CSharp)
                                 .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
                                 .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
                                 .WithMetadataReferences(References.Value);

        project = project.AddDocument("Candidates.cs", TypeArgumentKinds.Candidates).Project;
        project = project.AddDocument("Generated.cs", generated).Project;
        return project.AddDocument("Subject.cs", source).Project;
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        ReferenceAssemblies.Net.Net100.ResolveAsync(LanguageNames.CSharp, CancellationToken.None).GetAwaiter().GetResult().Add(RealTypeKit.Reference));

    private static async Task<Compilation> CompileAsync(Project project) =>
        (await project.GetCompilationAsync(TestContext.Current.CancellationToken)).ShouldNotBeNull();
}
