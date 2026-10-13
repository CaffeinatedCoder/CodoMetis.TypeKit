using Microsoft.CodeAnalysis;

namespace CodoMetis.TypeKit.Tests;

/// <summary>
/// <c>FirstOrNone</c> and <c>FirstOrError</c> on an <see cref="IQueryable{T}"/> hand the condition and
/// the limit to the query provider. On the <see cref="IEnumerable{T}"/> overloads, the only ones there
/// were, a query was read in full and its condition run in memory; the EF Core tests show the SQL.
/// </summary>
public sealed class QueryableOptionTests
{
    private static string Consumer(string member) =>
        $$"""
          using System;
          using System.Linq;
          using CodoMetis.TypeKit;

          static class Consumer
          {
              static {{member}}
          }
          """;

    public static TheoryData<string, string> Calls => new()
    {
        { "Option<int> First(IQueryable<int> query) => query.FirstOrNone(x => x > 1);", "Option" },
        { "Option<int> First(IQueryable<int> query) => query.FirstOrNone();", "Option" },
        { "Result<int, string> First(IQueryable<int> query) => query.FirstOrError(x => x > 1, \"none\");", "Result" },
        { "Option<int> First(IOrderedQueryable<int> query) => query.FirstOrNone(x => x > 1);", "Option" },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void A_query_binds_to_the_overload_that_takes_an_expression(string member, string declaringClass)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));
        compilation.Problems().ShouldBeEmpty();

        var (_, method) = compilation.TheOnlyCall();

        var block = method.ContainingType.ShouldNotBeNull();
        block.IsExtension.ShouldBeTrue();
        block.ContainingType.Name.ShouldBe(declaringClass);
        block.ExtensionParameter.ShouldNotBeNull().Type.OriginalDefinition.ToDisplayString().ShouldBe("System.Linq.IQueryable<T>");
        method.Parameters.Where(parameter => parameter.Name == "predicate")
              .ShouldAllBe(parameter => parameter.Type.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>");
    }

    /// <summary>A query has no last element until it is ordered; the overloads that would read every row are compile errors that name what to do instead.</summary>
    [Theory]
    [InlineData("Option<int> Last(IQueryable<int> query) => query.LastOrNone();", "FirstOrNone")]
    [InlineData("Option<int> Last(IQueryable<int> query) => query.LastOrNone(x => x > 1);", "FirstOrNone")]
    [InlineData("Result<int, string> Last(IQueryable<int> query) => query.LastOrError(x => x > 1, \"none\");", "FirstOrError")]
    public void LastOrNone_and_LastOrError_on_a_query_are_compile_errors(string member, string advice)
    {
        var compilation = ConsumerCompilation.Of(Consumer(member));

        compilation.ErrorIds().ShouldBe(["CS0619"], compilation.Problems());
        compilation.Problems().ShouldContain(advice);
        compilation.Problems().ShouldContain("AsEnumerable()");
    }

    [Fact]
    public void Reading_a_query_on_purpose_still_has_LastOrNone() =>
        ConsumerCompilation.Of(Consumer("Option<int> Last(IQueryable<int> query) => query.AsEnumerable().LastOrNone();")).Problems().ShouldBeEmpty();

    [Fact]
    public void A_query_answers_its_first_match_or_None()
    {
        IQueryable<int> query = new[] { 3, 1, 4, 1, 5 }.AsQueryable();

        query.FirstOrNone().ShouldBe(Option.Some(3));
        query.FirstOrNone(x => x > 3).ShouldBe(Option.Some(4));
        query.FirstOrNone(x => x > 9).ShouldBe(Option.None<int>());
        query.FirstOrError(x => x > 3, "none").ShouldBe(Result<int, string>.Success(4));
        query.FirstOrError(x => x > 9, "none").ShouldBe(Result<int, string>.Error("none"));
        Array.Empty<int>().AsQueryable().FirstOrNone().ShouldBe(Option.None<int>());
    }

    [Fact]
    public void A_null_first_element_a_null_condition_and_a_null_error_are_refused()
    {
        var query = new[] { (string)null! }.AsQueryable();

        Should.Throw<ArgumentNullException>(() => query.FirstOrNone());
        Should.Throw<ArgumentNullException>(() => new[] { "a" }.AsQueryable().FirstOrNone(null!)).ParamName.ShouldBe("predicate");
        Should.Throw<ArgumentNullException>(() => new[] { "a" }.AsQueryable().FirstOrError(x => x == "b", (string)null!)).ParamName.ShouldBe("error");
    }
}
