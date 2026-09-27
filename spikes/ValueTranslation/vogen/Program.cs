using Microsoft.EntityFrameworkCore;
using Vogen;

using var db = new Db();
Probe("VO == VO           ", () => db.Customers.Where(c => c.Email == Email.From("a@b")).ToQueryString());
Probe(".Value == literal  ", () => db.Customers.Where(c => c.Email.Value == "a@b").ToQueryString());
Probe(".Value.StartsWith  ", () => db.Customers.Where(c => c.Email.Value.StartsWith("a")).ToQueryString());
Probe("Select .Value      ", () => db.Customers.Select(c => c.Email.Value).ToQueryString());

static void Probe(string name, Func<string> query)
{
    try { Console.WriteLine($"[vogen] {name}: OK   {query().Replace('\n', ' ')}"); }
    catch (Exception e) { Console.WriteLine($"[vogen] {name}: FAIL {e.GetType().Name}: {e.Message.Split('\n')[0][..Math.Min(140, e.Message.Split('\n')[0].Length)]}"); }
}

[ValueObject<string>(conversions: Conversions.EfCoreValueConverter)]
public readonly partial struct Email;

public sealed class Customer { public int Id { get; set; } public Email Email { get; set; } }

public sealed class Db : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("Data Source=:memory:");
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<Customer>().Property(c => c.Email).HasConversion(new Email.EfCoreValueConverter());
}
