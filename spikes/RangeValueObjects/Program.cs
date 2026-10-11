using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using CodoMetis.TypeKit;
using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Serialization;
using Microsoft.EntityFrameworkCore;
using RangeValueObjects;
using Testcontainers.PostgreSql;

var jan = new DateOnly(2026, 1, 1);
var h1 = DateRange.CreateFinite(jan, new DateOnly(2026, 6, 30));
var q3 = DateRange.CreateFinite(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

Section("1. Generated surface");
foreach (var m in typeof(ContractPeriod).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
             .Where(m => m is not MethodInfo { IsSpecialName: true }).Select(m => $"{m.MemberType} {m.Name}").Distinct().Order())
    Console.WriteLine($"  {m}");
Console.WriteLine($"  interfaces: {string.Join(", ", typeof(ContractPeriod).GetInterfaces().Select(i => i.Name).Order())}");
Console.WriteLine($"  [TypeConverter]: {typeof(ContractPeriod).GetCustomAttribute<TypeConverterAttribute>()?.ConverterTypeName ?? "none"}");

Section("2. Create and the generated ways in");
Report("Create(h1)", () => ContractPeriod.Create(h1).Match(p => $"success {p}", f => $"error {f}"));
Report("Create(unbounded end)", () => ContractPeriod.Create(DateRange.CreateUnboundedEnd(jan)).Match(p => $"success {p}", f => $"error {f}"));
Report("Create(two years)", () => ContractPeriod.Create(DateRange.CreateFinite(jan, jan.AddYears(2))).Match(p => $"success {p}", f => $"error {f}"));
Report("Create(empty)", () => ContractPeriod.Create(DateRange.Empty).Match(p => $"success {p}", f => $"error {f}"));
Report("TryFrom(infinite)", () => ContractPeriod.TryFrom(DateRange.Infinite).IsSome().ToString());
Report("FromKnownGood(infinite)", () => ContractPeriod.FromKnownGood(DateRange.Infinite).ToString());
Report("Availability.From(infinite)", () => Availability.From(DateRange.Infinite).ToString());
Report("Availability.From(null!)", () => Availability.From(null!).ToString());

var period = ContractPeriod.FromKnownGood(h1);
Section("3. Text: ToString, Parse, interpolation, type converter");
Report("ToString()", () => period.ToString());
Report("$\"{period}\"", () => $"{period}");
Report("Parse(ToString())", () => (ContractPeriod.Parse(period.ToString(), null) == period).ToString());
Report("Parse(\"[2026-01-01,2026-03-31]\")", () => ContractPeriod.Parse("[2026-01-01,2026-03-31]", null).ToString());
Report("Parse(\"[2026-01-01,)\") (refused by Create)", () => ContractPeriod.Parse("[2026-01-01,)", null).ToString());
Report("Parse(\"not a range\")", () => ContractPeriod.Parse("not a range", null).ToString());
Report("TryParse(\"[2026-01-01,)\")", () => ContractPeriod.TryParse("[2026-01-01,)", null, out _).ToString());
Report("TypeConverter from string", () => TypeDescriptor.GetConverter(typeof(ContractPeriod)).ConvertFromInvariantString("[2026-01-01,2026-03-31]")!.ToString()!);
Report("TypeConverter from \"(,2026-01-01]\"", () => TypeDescriptor.GetConverter(typeof(ContractPeriod)).ConvertFromInvariantString("(,2026-01-01]")!.ToString()!);
Report("DateRange's own TypeConverter", () => TypeDescriptor.GetConverter(typeof(DateRange)).GetType().Name);
foreach (var text in new[] { "[2026-01-01,2026-06-30]", "[2026-01-01,)", "not a range" })
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Period"] = text, ["Range"] = text }).Build();
    Report($"configuration binding {text}", () =>
    {
        var settings = configuration.Get<ContractSettings>()!;
        return $"Period {settings.Period?.ToString() ?? "null"}, Range {settings.Range?.ToString() ?? "null"}";
    });
}

Section("4. Equality and ordering");
Report("equal ranges, equal value objects", () => (ContractPeriod.FromKnownGood(DateRange.CreateFinite(jan, new DateOnly(2026, 6, 30))) == period).ToString());
Report("hash codes agree", () => (ContractPeriod.FromKnownGood(DateRange.CreateFinite(jan, new DateOnly(2026, 6, 30))).GetHashCode() == period.GetHashCode()).ToString());
Report("IComparable<ContractPeriod>", () => (period is IComparable<ContractPeriod>).ToString());

Section("5. JSON parity (decision 26)");
// Without registering ValueRanges' converters (9.0.0 puts [JsonConverter] on every range type), and with them.
foreach (var (name, options) in new[]
         {
             ("default", JsonSerializerOptions.Default),
             ("web", JsonSerializerOptions.Web),
             ("default+ranges", new JsonSerializerOptions().AddRangeConverters()),
             ("web+ranges", new JsonSerializerOptions(JsonSerializerDefaults.Web).AddRangeConverters()),
         })
{
    Console.WriteLine($"  -- {name}");
    Report("value object", () => JsonSerializer.Serialize(period, options));
    Report("wrapped DateRange", () => JsonSerializer.Serialize(h1, options));
    Report("object holding each", () => JsonSerializer.Serialize(new { Period = period }, options) + "  |  " + JsonSerializer.Serialize(new { Period = h1 }, options));
    Report("read back", () => (JsonSerializer.Deserialize<ContractPeriod>(JsonSerializer.Serialize(period, options), options) == period).ToString());
    Report("dictionary key, value object", () => JsonSerializer.Serialize(new Dictionary<ContractPeriod, int> { [period] = 1 }, options));
    Report("dictionary key, DateRange", () => JsonSerializer.Serialize(new Dictionary<DateRange, int> { [h1] = 1 }, options));
    Report("read \"[2026-01-01,)\" (refused by Create)", () => JsonSerializer.Deserialize<ContractPeriod>("\"[2026-01-01,)\"", options).ToString());
    Report("read \"[2026-13-01,2026-14-01]\"", () => JsonSerializer.Deserialize<ContractPeriod>("\"[2026-13-01,2026-14-01]\"", options).ToString());
    Report("DateRange read \"[2026-13-01,2026-14-01]\"", () => JsonSerializer.Deserialize<DateRange>("\"[2026-13-01,2026-14-01]\"", options)!.ToString());
    Report("read null", () => JsonSerializer.Deserialize<ContractPeriod>("null", options).ToString());
    Report("Availability? read null", () => JsonSerializer.Deserialize<Availability?>("null", options)?.ToString() ?? "null");
    Report("Availability read \"(,)\"", () => JsonSerializer.Deserialize<Availability>("\"(,)\"", options).ToString());
    Report("Availability read \"empty\"", () => JsonSerializer.Deserialize<Availability>("\"empty\"", options).ToString());
}

Section("6. EF Core on PostgreSQL");
await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
await postgres.StartAsync();
var cs = postgres.GetConnectionString();

foreach (var (order, configure) in new (string, Action<DbContextOptionsBuilder>)[]
         {
             ("UseNpgsql(UseValueRanges) then UseTypeKit", o => o.UseNpgsql(cs, n => n.UseValueRanges()).UseTypeKit().EnableServiceProviderCaching(false)),
             ("UseTypeKit then UseNpgsql(UseValueRanges)", o => o.UseTypeKit().UseNpgsql(cs, n => n.UseValueRanges()).EnableServiceProviderCaching(false)),
         })
{
    Console.WriteLine($"\n  -- registration: {order}");
    await using var db = new ContractsDb(configure);
    var et = db.Model.FindEntityType(typeof(Contract))!;
    foreach (var p in et.GetProperties())
        Console.WriteLine($"  {p.Name,-14} column {p.GetColumnType(),-12} converter {p.GetTypeMapping().Converter?.GetType().Name ?? "-"}  mapping {p.GetTypeMapping().GetType().Name}");
}

await using (var db = new ContractsDb(o => o.UseNpgsql(cs, n => n.UseValueRanges()).UseTypeKit()))
{
    Console.WriteLine("\n  -- create script");
    foreach (var line in db.Database.GenerateCreateScript().Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        Console.WriteLine($"  {line}");
    await db.Database.EnsureCreatedAsync();

    db.Contracts.AddRange(
        new Contract { Period = period, Availability = Availability.From(DateRange.CreateUnboundedEnd(jan)) },
        new Contract { Period = ContractPeriod.FromKnownGood(q3), Availability = null });
    await db.SaveChangesAsync();
    // A row the rule refuses, written past the application (as a migration or another writer might).
    await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Contracts" ("Period") VALUES ('[2026-01-01,)'::daterange)""");
}

await using (var db = new ContractsDb(o => o.UseNpgsql(cs, n => n.UseValueRanges()).UseTypeKit()))
{
    Console.WriteLine("\n  -- round trip");
    foreach (var c in await db.Contracts.OrderBy(c => c.Id).ToListAsync())
        Console.WriteLine($"  #{c.Id}: Period {c.Period} (equal to written: {c.Period == period || c.Period.Value == q3}), Availability {c.Availability?.ToString() ?? "null"}, Revalidate {c.Period.Revalidate().Match(_ => "ok", f => f.ToString())}");

    var day = new DateOnly(2026, 3, 15);
    var other = DateRange.CreateFinite(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 15));
    var periodParam = period;

    Console.WriteLine("\n  -- queries");
    await Query("Period.Value.Contains(day)", db.Contracts.Where(c => c.Period.Value.Contains(day)).Select(c => c.Id));
    await Query("Period.Value.Overlaps(other)", db.Contracts.Where(c => c.Period.Value.Overlaps(other)).Select(c => c.Id));
    await Query("Period.Value.IsFinite()", db.Contracts.Where(c => c.Period.Value.IsFinite()).Select(c => c.Id));
    await Query("OrderBy Period.Value.LowerBound()", db.Contracts.OrderByDescending(c => c.Period.Value.LowerBound()).Select(c => c.Id));
    await Query("Select Period.Value.Intersect(other)", db.Contracts.OrderBy(c => c.Id).Select(c => c.Period.Value.Intersect(other)));
    await Query("Period == value-object parameter", db.Contracts.Where(c => c.Period == periodParam).Select(c => c.Id));
    await Query("Period.Value == DateRange parameter", db.Contracts.Where(c => c.Period.Value == h1).Select(c => c.Id));
    await Query("Select Period", db.Contracts.OrderBy(c => c.Id).Select(c => c.Period));
    await Query("Select Period.Value", db.Contracts.OrderBy(c => c.Id).Select(c => c.Period.Value));
    await Query("Availability.ValueOrNull()!.Contains(day)", db.Contracts.Where(c => c.Availability.ValueOrNull()!.Contains(day)).Select(c => c.Id));
    await Query("Availability.GetValue().Contains(day)", db.Contracts.Where(c => c.Availability!.Value.GetValue().Contains(day)).Select(c => c.Id));
    await Query("Availability.Value.Value.Contains(Period.Value)", db.Contracts.Where(c => c.Availability != null && c.Availability.Value.Value.Contains(c.Period.Value)).Select(c => c.Id));
    await Query("RangeAgg over Period.Value", db.Contracts.GroupBy(c => 1).Select(g => g.Select(c => c.Period.Value).RangeAgg()));
}

Section("7. ASP.NET Core: binding and the OpenAPI document");
// ValueRanges' OpenAPI transformer is documentation to copy (ValueRangesSchemaTransformer.cs), registered before and after TypeKit's.
foreach (var (host, withRanges, openApi) in new (string, bool, Action<Microsoft.AspNetCore.OpenApi.OpenApiOptions>)[]
         {
             ("nothing registered", false, o => o.AddTypeKit()),
             ("AddRangeConverters()", true, o => o.AddTypeKit()),
             ("AddTypeKit() then ValueRangesSchemaTransformer", false, o => o.AddTypeKit().AddSchemaTransformer<ValueRangesSchemaTransformer>()),
             ("ValueRangesSchemaTransformer then AddTypeKit()", false, o => o.AddSchemaTransformer<ValueRangesSchemaTransformer>().AddTypeKit()),
         })
{
    Console.WriteLine($"\n  -- host: {host}");
    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    if (withRanges) builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AddRangeConverters());
    builder.Services.AddOpenApi(openApi);
    await using var app = builder.Build();
    app.MapOpenApi();
    app.MapPost("/contracts", (ContractDto body) => body);
    app.MapPost("/by-period", (Dictionary<ContractPeriod, int> body) => body);
    app.MapGet("/contracts", (ContractPeriod period) => period.ToString());
    app.MapGet("/ranges", (DateRange range) => range.ToString());
#pragma warning disable ASP0020 // a value object declared in this project (src/CodoMetis.TypeKit.AspNetCore/README.md)
    app.MapGet("/period-array", ([Microsoft.AspNetCore.Mvc.FromQuery] ContractPeriod[] periods) => periods.Length);
#pragma warning restore ASP0020
    app.MapGet("/range-array", ([Microsoft.AspNetCore.Mvc.FromQuery] DateRange[] ranges) => ranges.Length);
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

    var doc = System.Text.Json.Nodes.JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
    var schemas = doc["components"]?["schemas"]?.AsObject();
    foreach (var name in new[] { "ContractPeriod", "DateRange", "ContractDto" })
        Console.WriteLine($"  schema {name,-15} {schemas?[name]?.ToJsonString() ?? "(none)"}");
    Console.WriteLine($"  query parameter  {doc["paths"]!["/contracts"]!["get"]!["parameters"]![0]!["schema"]!.ToJsonString()}");
    Console.WriteLine($"  DateRange query  {doc["paths"]!["/ranges"]!["get"]!["parameters"]![0]!["schema"]!.ToJsonString()}");
    Console.WriteLine($"  query array, value object  {doc["paths"]!["/period-array"]!["get"]!["parameters"]![0]!["schema"]!.ToJsonString()}");
    Console.WriteLine($"  query array, DateRange     {doc["paths"]!["/range-array"]!["get"]!["parameters"]![0]!["schema"]!.ToJsonString()}");
    using (var r = await client.GetAsync("/period-array?periods=" + Uri.EscapeDataString("[2026-01-01,2026-06-30]") + "&periods=" + Uri.EscapeDataString("[2026-07-01,2026-09-30]")))
        Console.WriteLine($"  GET /period-array (two)    {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");

    foreach (var body in new[]
             {
                 """{"period":"[2026-01-01,2026-06-30]","range":"[2026-01-01,)"}""",
                 """{"period":"[2026-01-01,)","range":"[2026-01-01,)"}""",
             })
    {
        using var response = await client.PostAsync("/contracts", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"  POST {body,-62} {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    foreach (var body in new[] { """{"[2026-01-01,2026-06-30]":1}""", """{"[2026-01-01,)":1}""" })
    {
        using var response = await client.PostAsync("/by-period", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Console.WriteLine($"  POST /by-period {body,-46} {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    foreach (var query in new[] { "[2026-01-01,2026-06-30]", "[2026-01-01,)" })
    {
        using var response = await client.GetAsync($"/contracts?period={Uri.EscapeDataString(query)}");
        Console.WriteLine($"  GET ?period={query,-24} {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    await app.StopAsync();
}

static async Task Query<T>(string name, IQueryable<T> query)
{
    Console.WriteLine($"  {name}");
    try
    {
        var sql = string.Join(" ", query.ToQueryString().Split('\n').Where(l => !l.StartsWith("--", StringComparison.Ordinal) && !l.StartsWith("SET", StringComparison.Ordinal)).Select(l => l.Trim()));
        Console.WriteLine($"    sql:    {sql}");
        Console.WriteLine($"    result: {string.Join(", ", (await query.ToListAsync()).Select(r => r?.ToString() ?? "null"))}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"    THROWS  {e.GetType().Name}: {e.Message.Split('\n')[0]}");
    }
}

static void Section(string title) => Console.WriteLine($"\n== {title}");

static void Report(string name, Func<string> probe)
{
    string result;
    try { result = probe(); }
    catch (Exception e) { result = $"THROWS {e.GetType().Name}: {e.Message.Split('\n')[0]}"; }
    Console.WriteLine($"  {name,-44} {result}");
}

namespace RangeValueObjects
{
    public sealed class Contract
    {
        public int Id { get; set; }
        public ContractPeriod Period { get; set; }
        public Availability? Availability { get; set; }
    }

    public sealed class ContractSettings
    {
        public ContractPeriod? Period { get; set; }
        public DateRange? Range { get; set; }
    }

    public sealed class ContractDto
    {
        public required ContractPeriod Period { get; init; }
        public required DateRange Range { get; init; }
    }

    public sealed class ContractsDb(Action<DbContextOptionsBuilder> configure) : DbContext
    {
        public DbSet<Contract> Contracts => Set<Contract>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => configure(options);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Contract>().HasIndex(c => c.Period).HasMethod("gist");
    }
}
