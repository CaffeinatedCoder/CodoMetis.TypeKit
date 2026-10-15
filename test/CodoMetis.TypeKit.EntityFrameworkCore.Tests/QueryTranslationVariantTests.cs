using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.TypeKit.EntityFrameworkCore.Tests.Variants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// Query translation across the variant catalogue of what a query reads of a value object (<see cref="QueryShapes"/>):
/// every kind, accessor, site, converter and operation, run on SQLite and translated for PostgreSQL, each
/// against the same query over an entity that holds the wrapped type, and against the same query over the
/// objects in memory.
/// </summary>
/// <remarks>
/// <para>
/// The oracle is the package README's "Queries" section and docs/ef-core.md, written per case
/// (<see cref="Expect"/>):
/// </para>
/// <list type="bullet">
/// <item><description>
/// "<c>.Value</c>, <c>GetValue()</c> and <c>ValueOrNull()</c> translate to the column itself", and "a value
/// object compares to a parameter or a column directly": the query answers the rows the same query over the
/// wrapped type answers, and where the operand is a table's column, which "is re-typed, not cast", with the
/// same SQL. Where EF refuses the wrapped type's query, the value object's may be refused too, and if it
/// answers, it answers as memory does.
/// </description></item>
/// <item><description>
/// "<c>.Value</c> translates only where the column holds the underlying value": on a column a converter of the
/// application's own stores, a query that reads it fails with "could not be translated", and "comparing the
/// value object itself goes through that converter and still works". How EF orders what such a converter
/// stores is EF's, and claimed nowhere.
/// </description></item>
/// <item><description>
/// "A null passes no <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c> in the database": a nullable
/// record-class value object compared with <c>&lt;</c> or <c>&lt;=</c> answers as the same query over the
/// wrapped type, which leaves out the null rows the generated operators keep in memory.
/// </description></item>
/// <item><description>
/// "Your own <c>CompareTo(TSelf)</c> can't run in the database": a comparison, an ordering, a <c>Min</c> or a
/// <c>Max</c> of such a value object is refused, naming it, in a query and in a query filter alike, and the
/// same by <c>.Value</c> is the column's order.
/// </description></item>
/// </list>
/// </remarks>
public sealed class QueryTranslationVariantTests
{
    /// <summary>One argument per axis: xunit cuts a string argument at 50 characters in a case's name, and the cases would share names.</summary>
    public static TheoryData<string, Conversion, Site, Accessor, Op> Cases
    {
        get
        {
            var cases = new TheoryData<string, Conversion, Site, Accessor, Op>();
            foreach (var @case in QueryShapes.All()) cases.Add(@case.Kind.Name, @case.Conversion, @case.Site, @case.Accessor, @case.Op);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_query_over_a_value_object_answers_as_the_same_query_over_the_wrapped_type(string kind, Conversion conversion, Site site, Accessor accessor, Op op)
    {
        var @case = QueryShapes.Find(new QueryCase(WrappedKinds.Named(kind), conversion, site, accessor, op).Id);
        var expectation = Expect(@case);
        if (expectation is Unclaimed unclaimed) Assert.Skip(unclaimed.Reason);
        if (expectation is OpenFinding finding) Assert.Skip($"open finding: {finding.Reason}");

        var valueObject = Run(@case, valueObject: true);

        switch (expectation)
        {
            case RefusedOrdering:
                Refusal(valueObject, "its ordering is its own CompareTo").ShouldContain(@case.Kind.ValueObject.Name);
                return;
            case RefusedForeign when valueObject.Refusal is null:
                valueObject.Rows.ShouldBe(Memory(@case).Rows, "refused, or evaluated in memory where EF evaluates a projection after the query");
                return;
            case RefusedForeign:
                Refusal(valueObject, "could not be translated");
                return;
            case NullsLeftOut:
                valueObject.Refusal.ShouldBeNull();
                var nulls = NullRows(@case);
                nulls.ShouldNotBeEmpty("the seed holds a row whose value object is null");
                Memory(@case).Rows.ShouldBe([.. valueObject.Rows!.Concat(nulls).Order(StringComparer.Ordinal)],
                    "the rows memory keeps are the query's and the null rows, which the generated operators sort first");

                // A string has no < to write the wrapped type's query with; memory is no oracle here.
                if (!new QueryBuilder(@case, valueObject: false).CanBuild) return;
                break;
        }

        var plain = new QueryBuilder(@case, valueObject: false).CanBuild ? Run(@case, valueObject: false) : null;
        if (valueObject.Refusal is not null)
        {
            if (plain is { Refusal: null }) throw new ShouldAssertException($"refused where the same query over the wrapped type answers [{string.Join(", ", plain.Rows!)}]: {valueObject.Refusal}");
            if (plain is null) throw new ShouldAssertException($"refused, where memory answers [{string.Join(", ", Memory(@case).Rows!)}]: {valueObject.Refusal}");
            return;
        }

        if (plain is { Refusal: null })
        {
            valueObject.Rows.ShouldBe(plain.Rows, $"the rows of the same query over the wrapped type; memory answers [{string.Join(", ", Memory(@case).Rows!)}]");
            if (ClaimsTheColumn(@case) && plain.Sql is not null)
                valueObject.Sql.ShouldBe(plain.Sql, "the SQL of the same query over the wrapped type");
        }
        else
        {
            valueObject.Rows.ShouldBe(Memory(@case).Rows, $"the rows memory answers, where the wrapped type's query is {(plain is null ? "not written" : "refused")}");
        }

        // PostgreSQL translates where it would refuse what SQLite runs: an element column re-typed to the
        // wrapped type, for one, threw there alone.
        if (plain is null || PostgreSql(@case, valueObject: false) is not { Refusal: null } wrapped) return;
        var translated = PostgreSql(@case, valueObject: true)!;
        if (translated.Refusal is not null)
            throw new ShouldAssertException($"PostgreSQL refuses what it translates over the wrapped type: {translated.Refusal}");
        if (ClaimsTheColumn(@case))
            translated.Sql.ShouldBe(wrapped.Sql, "PostgreSQL's SQL of the same query over the wrapped type");
    }

    /// <summary>A guard against a vacuous run: the catalogue's floor, every kind, site and accessor in it, and the refusals it expects.</summary>
    [Fact]
    public void The_catalogue_reaches_every_kind_site_accessor_and_converter()
    {
        var all = QueryShapes.All().ToList();

        all.Count.ShouldBeGreaterThan(2500);
        all.Select(@case => @case.Kind).Distinct().Count().ShouldBe(WrappedKinds.Queries.Length);
        all.Select(@case => @case.Site).Distinct().Count().ShouldBe(Enum.GetValues<Site>().Length);
        all.Select(@case => @case.Accessor).Distinct().Count().ShouldBe(Enum.GetValues<Accessor>().Length);
        all.Select(@case => @case.Conversion).Distinct().Count().ShouldBe(Enum.GetValues<Conversion>().Length);
        all.Count(@case => Expect(@case) is RefusedOrdering).ShouldBeGreaterThan(100);
        all.Count(@case => Expect(@case) is RefusedForeign).ShouldBeGreaterThan(50);
        all.Count(@case => Expect(@case) is NullsLeftOut).ShouldBeGreaterThan(5);
    }

    private abstract record Expectation;

    /// <summary>Answers as the same query over the wrapped type.</summary>
    private sealed record Same : Expectation;

    /// <summary>Refused, naming the value object whose own <c>CompareTo</c> the database cannot run.</summary>
    private sealed record RefusedOrdering : Expectation;

    /// <summary>
    /// Refused by EF, "could not be translated": the column holds what a converter of the application's own
    /// stores. A final projection EF evaluates in memory, after reading the value object through that converter,
    /// answers as memory does, which is no wrong answer.
    /// </summary>
    private sealed record RefusedForeign : Expectation;

    /// <summary>
    /// Answers as the same query over the wrapped type, where a null passes no comparison, which is fewer rows
    /// than memory's: a record class's generated operators sort null first (decision 98).
    /// </summary>
    private sealed record NullsLeftOut : Expectation;

    /// <summary>A divergence the catalogue found and no decision settles yet: named here, never dropped from the catalogue.</summary>
    private sealed record OpenFinding(string Reason) : Expectation;

    /// <summary>No sentence of the docs says what the case answers.</summary>
    private sealed record Unclaimed(string Reason) : Expectation;

    private static Expectation Expect(QueryCase @case)
    {
        if (@case.IsForeign)
        {
            if (@case.ReadsWrapped) return new RefusedForeign();
            if (!@case.Orders) return new Same();
            return new Unclaimed("EF compares and orders what a converter of the application's own stores; the docs claim only that comparing the value object itself works");
        }

        if (@case.Kind.Ordering == Ordering.HandWritten && @case is { ReadsWrapped: false, Orders: true }) return new RefusedOrdering();

        // The generated operators of a record class sort null first, so in memory null < x is true; SQL's
        // "N" < @x is unknown for a null N, as it is for the wrapped type's lifted operator.
        if (!@case.Kind.ValueObject.IsValueType && @case is { Site: Site.Nullable, Accessor: Accessor.Itself, Op: Op.LessThan or Op.LessOrEqual })
            return new NullsLeftOut();

        return new Same();
    }

    /// <summary>A table's column read through an accessor is "re-typed, not cast": the same SQL as the wrapped type's column.</summary>
    private static bool ClaimsTheColumn(QueryCase @case) =>
        @case.ReadsWrapped && @case.Conversion is Conversion.TypeKit or Conversion.ExplicitTypeKit
     && @case.Site is Site.Column or Site.Nullable or Site.Navigation or Site.QueryFilter or Site.NamedQueryFilter;

    /// <summary>The rows of the case's seed whose nullable value object is null, as a query over the table answers them.</summary>
    private static string[] NullRows(QueryCase @case) =>
    [
        .. Seed.Entities(new QueryBuilder(@case, valueObject: true), @case.Kind, valueObject: true)
               .Cast<object>()
               .Where(row => row.GetType().GetProperty("N")!.GetValue(row) is null)
               .Select(row => Text(row.GetType().GetProperty("Id")!.GetValue(row))),
    ];

    private static string Refusal(Answer answer, string expected)
    {
        answer.Refusal.ShouldNotBeNull($"answered [{string.Join(", ", answer.Rows ?? [])}] where the docs say the query is refused");
        answer.Refusal.ShouldBeOfType<InvalidOperationException>();
        answer.Refusal.Message.ShouldContain(expected);
        return answer.Refusal.Message;
    }

    private sealed record Answer(string[]? Rows, string? Sql, Exception? Refusal);

    /// <summary>One in-memory SQLite database per model, seeded once and only read.</summary>
    private static readonly ConcurrentDictionary<string, Lazy<(SqliteConnection Connection, Exception? Refusal)>> Databases = new();

    private static Answer Run(QueryCase @case, bool valueObject)
    {
        var builder = new QueryBuilder(@case, valueObject);
        var key = @case.IsFilter
                      ? $"{@case.Id} | {valueObject}"
                      : $"{@case.Kind.Name} | {(valueObject ? @case.Conversion : Conversion.TypeKit)} | {builder.Source.Name} | {valueObject}";
        var model = new QueryModel(key, @case, valueObject);
        var (connection, refusal) = Databases.GetOrAdd(key, _ => new Lazy<(SqliteConnection, Exception?)>(() => Seeded(model, builder))).Value;
        if (refusal is not null) return new Answer(null, null, refusal);

        using var db = QueryDb.Create(model, connection);
        try
        {
            var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(builder.Source).Invoke(db, null)!;
            var (query, ordered) = builder.Build(set.Expression);
            return Execute(set.Provider, query, ordered, sql: true);
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return new Answer(null, null, Unwrapped(exception));
        }
    }

    /// <summary>
    /// The query translated for PostgreSQL, without a server; <see langword="null"/> where no SQL text is
    /// written (a filter, which needs the model's database, and a <c>Min</c> or <c>Max</c>, which runs at once).
    /// </summary>
    private static Answer? PostgreSql(QueryCase @case, bool valueObject)
    {
        if (@case.IsFilter) return null;

        var builder = new QueryBuilder(@case, valueObject);
        var model = new QueryModel($"{@case.Kind.Name} | {(valueObject ? @case.Conversion : Conversion.TypeKit)} | {builder.Source.Name} | {valueObject} | npgsql", @case, valueObject);
        using var db = QueryDb.OnPostgreSql(model);
        try
        {
            var set = (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(builder.Source).Invoke(db, null)!;
            var (query, _) = builder.Build(set.Expression);
            return typeof(IQueryable).IsAssignableFrom(query.Type) ? new Answer(null, set.Provider.CreateQuery(query).ToQueryString(), null) : null;
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return new Answer(null, null, Unwrapped(exception));
        }
    }

    /// <summary>The same query over the value objects in memory, with the query filter applied as EF would.</summary>
    private static Answer Memory(QueryCase @case)
    {
        var builder = new QueryBuilder(@case, valueObject: true);
        var source = Queryable.AsQueryable(Seed.Entities(builder, @case.Kind, valueObject: true));
        var root = @case.IsFilter
                       ? Expression.Call(typeof(Queryable), nameof(Queryable.Where), [builder.Source], source.Expression, Expression.Quote(builder.Filter()))
                       : source.Expression;
        var (query, ordered) = builder.Build(root);
        return Execute(source.Provider, query, ordered, sql: false);
    }

    private static (SqliteConnection, Exception?) Seeded(QueryModel model, QueryBuilder builder)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        try
        {
            using var db = QueryDb.Create(model, connection);
            db.Database.EnsureCreated();
            db.AddRange(Seed.Stored(builder, model.Case.Kind, model.ValueObject));
            db.SaveChanges();
            return (connection, null);
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            return (connection, Unwrapped(exception));
        }
    }

    private static Answer Execute(IQueryProvider provider, Expression query, bool ordered, bool sql)
    {
        if (!typeof(IQueryable).IsAssignableFrom(query.Type))
            return new Answer([Text(provider.Execute(query))], null, null);

        var queryable = provider.CreateQuery(query);
        var rows = new List<string>();
        foreach (var row in (System.Collections.IEnumerable)queryable) rows.Add(Text(row));
        if (!ordered) rows.Sort(StringComparer.Ordinal);

        return new Answer([.. rows], sql ? queryable.ToQueryString() : null, null);
    }

    /// <summary>An entity as its key, anything else as <see cref="WrappedKind.Text"/> writes it.</summary>
    private static string Text(object? value) =>
        value?.GetType() is { IsGenericType: true } type && type.GetProperty("Id") is { } id && type.Namespace == typeof(Seed).Namespace && type.Name != "Box`1"
            ? $"#{id.GetValue(value)}"
            : WrappedKind.Text(value);

    private static Exception Unwrapped(Exception exception) => exception is TargetInvocationException { InnerException: { } inner } ? Unwrapped(inner) : exception;
}
