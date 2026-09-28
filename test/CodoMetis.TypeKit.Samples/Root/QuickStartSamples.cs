// sample: README/quick-start-declaration
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

// end sample
using System.Text.Json;
using System.Text.Json.Nodes;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ReadmeSamples.Root.QuickStart;

// sample: README/quick-start-declaration
public enum EmailFault { Blank, NoAt }

public readonly partial record struct Email : IValidatedValue<Email, string, EmailFault>
{
    public static Result<Email, EmailFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(EmailFault.Blank);
        if (!value.Contains('@')) return Result.Error(EmailFault.NoAt);

        return new Email(value.Trim());
    }
}
// end sample

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct CustomerId : IValue<Guid>;

public sealed class Customer
{
    public required CustomerId Id { get; init; }

    public required Email Email { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
}

public static class QuickStart
{
    public static void Use(string input)
    {
        // sample: README/quick-start-use
        var id = OrderId.New();                                   // a version 7 Guid

        Result<Email, EmailFault> created = Email.Create(input);  // the fault says which rule refused it
        Option<Email> maybe = Email.TryFrom(input);               // is it valid?
        Email known = Email.FromKnownGood("ops@example.com");     // a literal you vouch for; throws otherwise

        string reply = created.Match(
            email => $"Welcome, {email}",
            fault => $"Please check the address ({fault})");

        JsonSerializer.Serialize(known);                          // "ops@example.com"
        JsonSerializer.Deserialize<Email>("\"nobody\"");          // JsonException naming Email and NoAt, never the text
        Email.Parse("ops@example.com", null);                     // IParsable, so it binds as a route or query parameter
        // end sample
    }

    public static void UseTheSatellites(IServiceCollection services, string connectionString, ShopDb db)
    {
        // sample: README/quick-start-satellites
        services.AddDbContext<ShopDb>(options => options.UseNpgsql(connectionString).UseTypeKit());

        db.Customers.Where(c => c.Email.Value.EndsWith("@example.com"));        // WHERE c."Email" LIKE '%@example.com'

        services.AddOpenApi(options => options.AddTypeKit());                    // OrderId: {"type":"string","format":"uuid"}
        // end sample
    }

}

public sealed class QuickStartTests
{
    private const string Readme = "README.md";

    [Fact]
    public void New_makes_a_version_7_Guid() => OrderId.New().Value.Version.ShouldBe(7);

    [Fact]
    public void The_fault_says_which_rule_refused_it()
    {
        Email.Create("nobody").TryGetValue(out _, out var noAt).ShouldBeFalse();
        Email.Create(" ").TryGetValue(out _, out var blank).ShouldBeFalse();

        noAt.ShouldBe(EmailFault.NoAt);
        blank.ShouldBe(EmailFault.Blank);
    }

    [Fact]
    public void FromKnownGood_throws_otherwise() => Should.Throw<InvalidOperationException>(() => Email.FromKnownGood("nobody"));

    [Fact]
    public void The_JSON_is_what_the_comments_say()
    {
        var known = Email.FromKnownGood("ops@example.com");

        JsonSerializer.Serialize(known).ShouldBeShownIn(Readme, json => json);
        SampleOutputs.ShouldRefuseAsShownIn(() => JsonSerializer.Deserialize<Email>("\"nobody\""), Readme, input: "nobody");
    }

    [Fact]
    public void Email_is_IParsable() => ParseAs<Email>("ops@example.com").ShouldBe(Email.FromKnownGood("ops@example.com"));

    [Fact]
    public void The_query_is_the_SQL_the_comment_says()
    {
        using var db = new ShopDb(SampleOutputs.NpgsqlWithoutServer<ShopDb>());

        db.Customers.Where(c => c.Email.Value.EndsWith("@example.com")).ShouldBeTheSqlShownIn(Readme);
    }

    /// <summary>The sample itself configures the host; the endpoint only gives the document an order id to describe.</summary>
    [Fact]
    public async Task The_document_describes_OrderId_as_the_comment_says()
    {
        using var db = new ShopDb(SampleOutputs.NpgsqlWithoutServer<ShopDb>());
        var shown = SampleOutputs.Shown(Readme, "services.AddOpenApi(options => options.AddTypeKit())").Split(": ", 2);

        var document = await SampleOutputs.OpenApiDocumentAsync(
            builder => QuickStart.UseTheSatellites(builder.Services, SampleOutputs.ConnectionStringWithoutServer, db),
            app => app.MapGet("/orders/latest", () => new Placed(OrderId.New())));

        JsonNode.DeepEquals(document.Component(shown[0]), JsonNode.Parse(shown[1])).ShouldBeTrue(
            $"{shown[0]} is {document.Component(shown[0]).ToJsonString()}, and {Readme} says {shown[1]}.");
    }

    private static T ParseAs<T>(string text) where T : IParsable<T> => T.Parse(text, null);

    public sealed record Placed(OrderId Id);
}
