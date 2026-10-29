using System.Collections.Concurrent;
using System.Globalization;
using CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static CodoMetis.TypeKit.EntityFrameworkCore.Tests.QueryTranslationVariantTests;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// The query-translation catalogue (<see cref="QueryShapes"/>) run on a real SQL Server: each query over a value
/// object against the same query over the wrapped type there, as <see cref="QueryTranslationVariantTests"/> runs
/// it on SQLite, and then against the same query over the objects in memory.
/// </summary>
/// <remarks>
/// An answer that differs from memory's is SQL Server's own only where SQLite answers as memory does: a null
/// <c>.Value</c> that throws in memory and is a NULL in SQL, or a query EF translates for neither, is the relational
/// providers' alike. Each difference that is SQL Server's alone is a fact of design/knowledge, named in
/// <see cref="Known"/>; any other fails.
/// </remarks>
[Collection(SqlServerCollection.Name)]
[Trait("Needs", "Docker")]
public sealed class SqlServerQueryVariantTests(SqlServer server)
{
    public static TheoryData<string, Conversion, Site, Accessor, Op> Cases => QueryTranslationVariantTests.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task On_SQL_Server_a_query_over_a_value_object_answers_as_the_same_query_over_the_wrapped_type_and_as_memory(
        string kind, Conversion conversion, Site site, Accessor accessor, Op op)
    {
        var @case = QueryShapes.Find(new QueryCase(WrappedKinds.Named(kind), conversion, site, accessor, op).Id);
        var expectation = Expect(@case);
        if (expectation is Unclaimed { SqliteOnly: false } unclaimed) Assert.Skip(unclaimed.Reason);
        if (expectation is OpenFinding finding) Assert.Skip($"open finding: {finding.Reason}");
        if (expectation is Unclaimed) expectation = new Same();

        var database = await server.DatabaseAsync("typekit_queries");
        AnswersAsTheWrappedType(@case, expectation, (c, valueObject) => Run(c, valueObject, database), out var answer, out _,
                                differs: (c, onSqlServer, memory) => Known(c, onSqlServer, memory) is not null);
        if (expectation is not (Same or NullsLeftOut)) return;

        // EF.Functions.Greatest and Least, which EF translates on SQL Server from compatibility level 160 alone (KB-053):
        // there the SQL is the wrapped type's, translated without a server.
        if (@case.Op is Op.Greatest or Op.Least && answer.Refusal is not null)
        {
            // Over a primitive collection's element EF refuses them on SQLite too, and a TimeSpan table is refused its rows (KB-051).
            if (Refusal(answer) is not DbUpdateException)
                answer.Refusal!.Message.ShouldMatch("is not supported because the query has switched to client-evaluation|could not be translated");
            AtLevel160(@case, database);
            return;
        }

        var memory = Memory(@case);
        if (Agree(answer, memory)) return;

        var sqlite = QueryTranslationVariantTests.Run(@case, valueObject: true);
        if (Agree(answer, sqlite)) return;

        // r.N!.Value.Value throws in memory on the row without one, where SQL reads a NULL, as ValueOrNull() does.
        if (memory.Refusal is not null && @case is { Site: Site.Nullable, Accessor: Accessor.ValueThroughTypeParameter }
         && QueryShapes.All().FirstOrDefault(other => other == @case with { Accessor = Accessor.ValueOrNull }) is { } orNull)
        {
            answer.Rows.ShouldBe(Run(orNull, valueObject: true, database).Rows, $"the same query through ValueOrNull(), where memory throws: {memory.Refusal.Message}");
            return;
        }

        if (Known(@case, answer, memory) is null)
            throw new ShouldAssertException($"SQL Server answers {Shown(answer)}, where memory answers {Shown(memory)} and SQLite {Shown(sqlite)}");
    }

    /// <summary>
    /// A guard against a vacuous run: the catalogue holds the cases each fact is about, and the facts that a case's
    /// answer decides (a decimal's scale, a refused aggregate) excuse no other answer.
    /// </summary>
    [Fact]
    public void Every_known_difference_is_one_the_catalogue_reaches()
    {
        Answer Rows(params string[] rows) => new(rows, null, null);

        QueryShapes.All().Select(@case => Known(@case, Rows("0"), Rows("1"))).OfType<string>().Distinct().ShouldBe([TextCollation, GuidOrder, TimeUnderADay], ignoreOrder: true);
        var decimals = QueryShapes.All().Where(@case => @case.Kind == WrappedKinds.Decimal).ToList();
        decimals.Any(@case => Known(@case, Rows("10.00"), Rows("10")) == DecimalScale).ShouldBeTrue("a decimal read back at another scale");
        decimals.All(@case => Known(@case, Rows("10.01"), Rows("10")) is null).ShouldBeTrue("a decimal read back as another number is no difference of scale");
        var refused = new Answer(null, null, new InvalidOperationException("Operand data type bit is invalid for max operator."));
        QueryShapes.All().Where(@case => @case.Kind == WrappedKinds.Boolean).Any(@case => Known(@case, refused, Rows("1")) == BitAggregate).ShouldBeTrue("a refused MAX of a bit");
    }

    private const string TextCollation = "KB-048: SQL Server compares and orders text by the database's collation, case-insensitive by default";

    private const string GuidOrder = "KB-049: SQL Server orders a uniqueidentifier by its last group first";

    private const string DecimalScale = "KB-050: a decimal reads back at its column's scale, decimal(18,2) unless configured";

    private const string TimeUnderADay = "KB-051: SQL Server's time holds less than a day, so a TimeSpan of a day or more is refused in a column and misread in JSON";

    private const string BitAggregate = "KB-052: SQL Server takes neither MAX nor MIN of a bit";

    /// <summary>
    /// The differences from memory that are SQL Server's own, each pinned in design/knowledge with a test of plain
    /// EF Core there (<c>test/CodoMetis.TypeKit.Facts</c>); null for any other.
    /// </summary>
    private static string? Known(QueryCase @case, Answer answer, Answer memory)
    {
        var wrapped = @case.Kind.Wrapped;
        if ((wrapped == typeof(string) || wrapped == typeof(char)) && @case.Orders && answer.Refusal is null) return TextCollation;
        if (wrapped == typeof(Guid) && @case.Orders && answer.Refusal is null) return GuidOrder;
        if (wrapped == typeof(decimal) && answer.Rows is { } rows && memory.Rows is { } expected && rows.Length == expected.Length
         && rows.Zip(expected).All(pair => pair.First == pair.Second || decimal.TryParse(pair.First, CultureInfo.InvariantCulture, out var read) && read == decimal.Parse(pair.Second, CultureInfo.InvariantCulture)))
            return DecimalScale;
        if (wrapped == typeof(TimeSpan) && (answer.Refusal is DbUpdateException || @case.Site is Site.JsonProperty or Site.CollectionElement)) return TimeUnderADay;
        if (wrapped == typeof(bool) && answer.Refusal?.Message.Contains("Operand data type bit is invalid", StringComparison.Ordinal) == true) return BitAggregate;
        return null;
    }

    /// <summary>The SQL of the case at compatibility level 160, over the value object and over the wrapped type, written without a server.</summary>
    private static void AtLevel160(QueryCase @case, string database)
    {
        Answer Translated(bool valueObject)
        {
            var builder = new QueryBuilder(@case, valueObject);
            using var db = QueryDb.OnSqlServer(new QueryModel($"{@case.Id} | {valueObject} | 160", @case, valueObject), database, compatibilityLevel: 160);
            try
            {
                var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(builder.Source).Invoke(db, null)!;
                return new Answer(null, set.Provider.CreateQuery(builder.Build(set.Expression).Query).ToQueryString(), null);
            }
            catch (Exception exception) when (exception is not ShouldAssertException)
            {
                return new Answer(null, null, Unwrapped(exception));
            }
        }

        if (!new QueryBuilder(@case, valueObject: false).CanBuild || Translated(valueObject: false) is not { Refusal: null } plain) return;

        var translated = Translated(valueObject: true);
        translated.Refusal.ShouldBeNull("refused at compatibility level 160, where the same query over the wrapped type translates");
        translated.Sql.ShouldBe(plain.Sql, "the SQL of the same query over the wrapped type at compatibility level 160");
    }

    private static Exception Refusal(Answer answer) => answer.Refusal ?? throw new ShouldAssertException($"answered {Shown(answer)}");

    /// <summary>The same rows, or both refused.</summary>
    private static bool Agree(Answer left, Answer right) =>
        left.Refusal is not null && right.Refusal is not null || left.Rows is { } rows && right.Rows is { } other && rows.SequenceEqual(other);

    /// <summary>The schema each model was seeded into, all in one database, seeded once and only read.</summary>
    private static readonly ConcurrentDictionary<string, Lazy<(QueryModel Model, Exception? Refusal)>> Schemas = new();

    private static int _schemas;

    private static Answer Run(QueryCase @case, bool valueObject, string database)
    {
        var builder = new QueryBuilder(@case, valueObject);
        var key = @case.IsFilter
                      ? $"{@case.Id} | {valueObject}"
                      : $"{@case.Kind.Name} | {(valueObject ? @case.Conversion : Conversion.TypeKit)} | {builder.Source.Name} | {valueObject}";
        var (model, refusal) = Schemas.GetOrAdd(key, _ => new Lazy<(QueryModel, Exception?)>(() =>
        {
            var seeded = new QueryModel(key, @case, valueObject, $"m{Interlocked.Increment(ref _schemas)}");
            return (seeded, Seeded(seeded, builder, database));
        })).Value;
        if (refusal is not null) return new Answer(null, null, refusal);

        using var db = QueryDb.OnSqlServer(model, database);
        try
        {
            var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(builder.Source).Invoke(db, null)!;
            var (query, ordered) = builder.Build(set.Expression);
            var answer = Execute(set.Provider, query, ordered, sql: true);

            // Each model has a schema of its own; the SQL compared is the rest.
            return answer with { Sql = answer.Sql?.Replace($"[{model.Schema}].", "", StringComparison.Ordinal) };
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return new Answer(null, null, Unwrapped(exception));
        }
    }

    private static Exception? Seeded(QueryModel model, QueryBuilder builder, string database)
    {
        try
        {
            using var db = QueryDb.OnSqlServer(model, database);
            db.GetService<IRelationalDatabaseCreator>().CreateTables();
            db.AddRange(Seed.Stored(builder, model.Case.Kind, model.ValueObject));
            db.SaveChanges();
            return null;
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return Unwrapped(exception);
        }
    }
}
