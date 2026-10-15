using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>The repository's identifiers are single terms, so a search for one finds it and nothing that merely shares a fragment.</summary>
public sealed class TokenizerTests
{
    [Theory]
    [InlineData("CMTK0004 is an error", "cmtk0004")]
    [InlineData("as decision 61 says", "adr:0061")]
    [InlineData("ADR-0068", "adr:0068")]
    [InlineData("see §9 and § 3.1.4", "§9")]
    [InlineData("see §9 and § 3.1.4", "§3.1.4")]
    [InlineData("IValidatedValue<TSelf, T, TFault> derives TryFrom", "ivalidatedvalue<tself,t,tfault>")]
    [InlineData("IValidatedValue<TSelf, T, TFault> derives TryFrom", "ivalidatedvalue")]
    [InlineData("Option<T>.Match takes two", "option<t>.match")]
    [InlineData("ReferenceHandler.Preserve is refused", "referencehandler.preserve")]
    [InlineData("ReferenceHandler.Preserve is refused", "preserve")]
    [InlineData("since 2.1.0", "2.1.0")]
    public void An_identifier_is_one_term(string text, string term) => Tokenizer.Terms(text).ShouldContain(term);

    [Fact]
    public void An_identifier_is_not_split_into_its_words()
    {
        var terms = Tokenizer.Terms("IValidatedValue<TSelf, T, TFault> and CMTK0004 and decision 61");

        terms.ShouldNotContain("validated");
        terms.ShouldNotContain("value");
        terms.ShouldNotContain("cmtk");
        terms.ShouldNotContain("tself", "a type parameter is no search term of its own");
    }

    [Fact]
    public void A_list_of_decisions_names_each()
    {
        var terms = Tokenizer.Terms("decisions 74, 75 and 76");
        terms.ShouldContain("adr:0074");
        terms.ShouldContain("adr:0075");
        terms.ShouldContain("adr:0076");
    }

    [Fact]
    public void A_query_meets_the_prose_across_inflections() =>
        Tokenizer.QueryTerms("preserve reference").ShouldBeSubsetOf(Tokenizer.Terms("Preserved references are refused"));

    [Fact]
    public void A_padded_number_in_a_query_is_a_record() => Tokenizer.QueryTerms("0061").ShouldContain("adr:0061");

    [Theory]
    [InlineData("CMTK0010", "cmtk0010")]
    [InlineData("61", "adr:0061")]
    [InlineData("0061", "adr:0061")]
    [InlineData("decision 61", "adr:0061")]
    [InlineData("ADR-0061", "adr:0061")]
    [InlineData("design/adr/0061-scope.md", "adr:0061")]
    public void An_id_is_parsed_in_any_spelling(string text, string key) => Ids.Parse(text).ShouldBe(key);

    [Theory]
    [InlineData("docs/json.md")]
    [InlineData("#preserve")]
    [InlineData("Preserve")]
    public void Anything_else_is_no_id(string text) => Ids.Parse(text).ShouldBeNull();
}
