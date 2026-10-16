using CodoMetis.TypeKit.ValueObjects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.TypeKit.EntityFrameworkCore.Tests;

/// <summary>
/// <c>.Value</c> read in a generic repository or base class, <c>where T : IValueObject&lt;T, string&gt;</c>, as C#
/// writes it: the tree holds <c>((IValueObject&lt;X, string&gt;)r.Name).Value</c>, the interface's member. It
/// translates to the column, as the value object's own <c>.Value</c> does, in a query and in a query filter, for
/// a record class and a record struct alike. <see cref="Variants.QueryShapes"/> holds the same shape over every
/// kind and site (<see cref="Variants.Accessor.ValueThroughTypeParameter"/>); these tests hold the tree the compiler
/// writes, which the catalogue builds by hand.
/// </summary>
public sealed class ValueThroughTypeParameterTests
{
    [Fact]
    public void A_record_class_read_through_a_type_parameter_is_the_column() =>
        StartsWithB<TypeParameterName>(db => db.Names).ShouldAnswer([2], "WHERE n.\"Name\" LIKE 'B%'");

    [Fact]
    public void A_record_struct_read_through_a_type_parameter_is_the_column() =>
        StartsWithB<TypeParameterCode>(db => db.Codes).ShouldAnswer([2], "WHERE c.\"Name\" LIKE 'B%'");

    [Fact]
    public void A_record_class_compared_through_a_type_parameter_is_the_column() =>
        Named<TypeParameterName>(db => db.Names, "Charlie").ShouldAnswer([3], "WHERE n.\"Name\" = @name");

    [Fact]
    public void A_record_struct_compared_through_a_type_parameter_is_the_column() =>
        Named<TypeParameterCode>(db => db.Codes, "Charlie").ShouldAnswer([3], "WHERE c.\"Name\" = @name");

    /// <summary>EF adds a query filter after the query's own tree is intercepted: the filter is rewritten when the model is built.</summary>
    [Fact]
    public void A_query_filter_written_in_a_generic_base_reads_the_column()
    {
        using var connection = Seeded();
        using var db = TypeParameterDb.Create(connection);

        db.Filtered.Select(row => row.Id).Order().ToList().ShouldBe([1, 2, 3]);
        db.Filtered.ToQueryString().ShouldContain("WHERE \"f\".\"Name\" <> 'hidden'");
    }

    private static Answered StartsWithB<T>(Func<TypeParameterDb, IQueryable<NamedRow<T>>> set)
        where T : IValueObject<T, string> =>
        Answer(set, query => query.Where(row => row.Name.Value.StartsWith("B")));

    private static Answered Named<T>(Func<TypeParameterDb, IQueryable<NamedRow<T>>> set, string name)
        where T : IValueObject<T, string> =>
        Answer(set, query => query.Where(row => row.Name.Value == name));

    private static Answered Answer<T>(Func<TypeParameterDb, IQueryable<NamedRow<T>>> set, Func<IQueryable<NamedRow<T>>, IQueryable<NamedRow<T>>> query)
        where T : IValueObject<T, string>
    {
        using var connection = Seeded();
        using var db = TypeParameterDb.Create(connection);
        var rows = query(set(db)).Select(row => row.Id).Order().ToArray();

        using var postgreSql = TypeParameterDb.OnPostgreSql();
        var sql = query(set(postgreSql)).ToQueryString();

        return new Answered(rows, sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..].Trim());
    }

    /// <summary>The rows SQLite answers, and the <c>WHERE</c> clause PostgreSQL's SQL ends with.</summary>
    private sealed record Answered(int[] Rows, string Sql)
    {
        public void ShouldAnswer(int[] rows, string where)
        {
            Rows.ShouldBe(rows);
            Sql.ShouldBe(where);
        }
    }

    private static SqliteConnection Seeded()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var db = TypeParameterDb.Create(connection);
        db.Database.EnsureCreated();
        string[] names = ["Alpha", "Bravo", "Charlie"];
        for (var i = 0; i < names.Length; i++)
        {
            db.Names.Add(new NamedRow<TypeParameterName> { Id = i + 1, Name = TypeParameterName.From(names[i]) });
            db.Codes.Add(new NamedRow<TypeParameterCode> { Id = i + 1, Name = TypeParameterCode.From(names[i]) });
            db.Filtered.Add(new FilteredRow { Id = i + 1, Name = TypeParameterName.From(names[i]) });
        }

        db.Filtered.Add(new FilteredRow { Id = 4, Name = TypeParameterName.From("hidden") });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        return connection;
    }
}

public sealed partial record TypeParameterName : IValue<string>;

public readonly partial record struct TypeParameterCode : IValue<string>;

public class NamedRow<T>
    where T : IValueObject<T, string>
{
    public int Id { get; set; }

    public required T Name { get; set; }
}

/// <summary>A generic base outside the model, so that its one row type is a root of its own and carries the filter.</summary>
public class FilterableRow<T>
    where T : IValueObject<T, string>
{
    public int Id { get; set; }

    public required T Name { get; set; }
}

public sealed class FilteredRow : FilterableRow<TypeParameterName>;

public sealed class TypeParameterDb(DbContextOptions<TypeParameterDb> options) : DbContext(options)
{
    public DbSet<NamedRow<TypeParameterName>> Names => Set<NamedRow<TypeParameterName>>();

    public DbSet<NamedRow<TypeParameterCode>> Codes => Set<NamedRow<TypeParameterCode>>();

    public DbSet<FilteredRow> Filtered => Set<FilteredRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NamedRow<TypeParameterName>>().ToTable("Names").Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<NamedRow<TypeParameterCode>>().ToTable("Codes").Property(row => row.Id).ValueGeneratedNever();

        var filtered = modelBuilder.Entity<FilteredRow>();
        filtered.ToTable("Filtered").Property(row => row.Id).ValueGeneratedNever();
        HidesTheHidden(filtered);
    }

    /// <summary>A filter a generic base class configures, for every row type it is given.</summary>
    private static void HidesTheHidden<TRow, T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TRow> entity)
        where TRow : FilterableRow<T>
        where T : IValueObject<T, string> =>
        entity.HasQueryFilter(row => row.Name.Value != "hidden");

    private static void HidesTheHidden(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<FilteredRow> entity) =>
        HidesTheHidden<FilteredRow, TypeParameterName>(entity);

    public static TypeParameterDb Create(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<TypeParameterDb>().UseSqlite(connection).UseTypeKit().Options);

    /// <summary>For SQL text only: <c>ToQueryString</c> translates without opening the connection.</summary>
    public static TypeParameterDb OnPostgreSql() =>
        new(new DbContextOptionsBuilder<TypeParameterDb>().UseNpgsql("Host=localhost;Database=none").UseTypeKit().Options);
}
