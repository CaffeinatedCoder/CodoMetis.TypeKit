using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// What a consumer's compiler says about <see cref="Patch{T}"/> and nullability. A value is never
/// null, a clear being the Null state, so every member hands the value as the value's type; over a
/// nullable type it handed a <c>string?</c> to <c>Match</c>, <c>Tap</c>, <c>Map</c> and <c>Traverse</c>, and
/// <c>p.Map(s =&gt; s.Trim())</c> on a <c>Patch&lt;string?&gt;</c> warned (CS8602) while
/// <c>Traverse(Email.Create)</c> did not match (CS8622), in a build where warnings are errors.
/// </summary>
/// <remarks>
/// Overload resolution ignores nullability, so no overload hands a <c>Patch&lt;string?&gt;</c> its value as a
/// <c>string</c> without warning a <c>Patch&lt;string&gt;</c> (design/patch.md has the measurements). <c>T</c>
/// is <c>notnull</c> instead, as an option's is: the nullable declaration is warned, once, where it is written.
/// </remarks>
public sealed class PatchNullabilityTests
{
    private const string Types = """
        using System;
        using CodoMetis.TypeKit;

        public enum F { Bad }
        public readonly record struct Code(string Value);
        public sealed record Name(string Value);
        """;

    /// <summary>Each member, on a patch over <paramref name="type"/>, its value used as non-null.</summary>
    private static string EveryMember(string type, string use, string check) =>
        $$"""
          {{Types}}

          public static class Checks
          {
              static Result<{{type}}, F> Check({{type}} value) => {{check}} ? value : Result.Error(F.Bad);

              public static void Run(Patch<{{type}}> patch)
              {
                  Patch<int> mapped = patch.Map(value => {{use}});
                  Result<Patch<{{type}}>, F> validated = patch.Traverse(Check);
                  int matched = patch.Match(value => {{use}}, () => 0, () => -1);
                  if (patch.TryGetValue(out var held)) _ = {{use.Replace("value", "held")}};
                  patch.Tap(value => _ = {{use}});
                  Option<{{type}}> option = patch.ToOption();
                  Patch<{{type}}> same = patch.IfPresent(value => _ = value);
              }
          }
          """;

    [Theory]
    [InlineData("string", "value.Length", "value.Length > 0")]
    [InlineData("Name", "value.Value.Length", "value.Value.Length > 0")]
    [InlineData("int", "value + 1", "value > 0")]
    [InlineData("Code", "value.Value.Length", "value.Value.Length > 0")]
    public void Every_member_hands_the_value_as_the_value_s_type_without_a_warning(string type, string use, string check) =>
        ConsumerCompilation.Of(EveryMember(type, use, check)).Problems().ShouldBeEmpty();

    /// <summary>
    /// The one place a nullable type argument is told: its declaration, each of them, whether a
    /// property, a parameter or a factory's type argument.
    /// </summary>
    [Fact]
    public void A_patch_over_a_nullable_type_is_warned_where_it_is_declared()
    {
        var compilation = ConsumerCompilation.Of($$"""
            {{Types}}

            public sealed class Request
            {
                public Patch<string?> Nickname { get; init; }
                public Patch<Name?> Owner { get; init; }
                public Patch<int?> Floor { get; init; }
            }

            public static class Clears
            {
                public static Patch<string?> Nickname() => Patch.Null<string?>();
            }
            """);

        var warnings = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning).ToList();

        warnings.Select(warning => warning.Id).Distinct().ShouldBe(["CS8714"], compilation.Problems());
        warnings.Select(warning => warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ShouldContain(message => message.Contains("'string?'"), compilation.Problems());
        warnings.Select(warning => warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ShouldContain(message => message.Contains("'Name?'"), compilation.Problems());
        warnings.Select(warning => warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ShouldContain(message => message.Contains("'int?'"), compilation.Problems());
        warnings.Count.ShouldBe(5, compilation.Problems());
    }

    /// <summary>
    /// Which member each call binds to, so that a member added later is seen to change a binding: the
    /// clear reaches <c>IfPresent</c> as null, through <c>T?</c>, over a class and over a struct.
    /// </summary>
    [Theory]
    [InlineData("string", "System.Action<string?>")]
    [InlineData("Name", "System.Action<Name?>")]
    [InlineData("int", "System.Action<int?>")]
    [InlineData("Code", "System.Action<Code?>")]
    public void IfPresent_hands_a_clear_over_as_null_over_a_class_and_a_struct(string type, string action)
    {
        var compilation = ConsumerCompilation.Of($$"""
            {{Types}}

            public static class Checks
            {
                public static void Run(Patch<{{type}}> patch) => patch.IfPresent(value => { });
            }
            """);

        compilation.Problems().ShouldBeEmpty();

        var tree   = compilation.SyntaxTrees.Single();
        var call   = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(call, TestContext.Current.CancellationToken).Symbol!;

        method.Parameters.Single().Type.ToDisplayString().ShouldBe(action);
    }
}
