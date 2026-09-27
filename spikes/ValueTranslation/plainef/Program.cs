using Microsoft.EntityFrameworkCore;

// Control: plain EF Core 10, a hand-written struct with a public Value and an implicit operator, no library.
using var db = new Db();
Probe(".Value == literal     ", () => db.Customers.Where(c => c.Email.Value == "a@b").ToQueryString());
Probe(".Value.StartsWith     ", () => db.Customers.Where(c => c.Email.Value.StartsWith("a")).ToQueryString());
Probe("(string)VO.StartsWith ", () => db.Customers.Where(c => ((string)c.Email).StartsWith("a")).ToQueryString());

static void Probe(string name, Func<string> query)
{
    try { Console.WriteLine($"[plain EF] {name}: OK   {query().Replace('\n', ' ')}"); }
    catch (Exception e) { var m = e.Message.Split('\n')[0]; Console.WriteLine($"[plain EF] {name}: FAIL {e.GetType().Name}: {m[..Math.Min(120, m.Length)]}"); }
}

public readonly record struct Email(string Value) { public static implicit operator string(Email e) => e.Value; }
public sealed class Customer { public int Id { get; set; } public Email Email { get; set; } }
public sealed class Db : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("Data Source=:memory:");
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<Customer>().Property(c => c.Email).HasConversion(e => e.Value, s => new Email(s));
}
