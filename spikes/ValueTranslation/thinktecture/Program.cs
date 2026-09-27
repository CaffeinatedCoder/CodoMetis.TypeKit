using Microsoft.EntityFrameworkCore;
using Thinktecture;

using var db = new Db();
Probe("VO == VO               ", () => db.Customers.Where(c => c.Email == Email.Create("a@b")).ToQueryString());
Probe("(string)VO == literal  ", () => db.Customers.Where(c => (string)c.Email == "a@b").ToQueryString());
Probe("((string)VO).StartsWith", () => db.Customers.Where(c => ((string)c.Email).StartsWith("a")).ToQueryString());
Probe("Select (string)VO      ", () => db.Customers.Select(c => (string)c.Email).ToQueryString());

static void Probe(string name, Func<string> query)
{
    try { Console.WriteLine($"[thinktecture] {name}: OK   {query().Replace('\n', ' ')}"); }
    catch (Exception e) { var m = e.Message.Split('\n')[0]; Console.WriteLine($"[thinktecture] {name}: FAIL {e.GetType().Name}: {m[..Math.Min(140, m.Length)]}"); }
}

[ValueObject<string>]
public readonly partial struct Email;

public sealed class Customer { public int Id { get; set; } public Email Email { get; set; } }

public sealed class Db : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("Data Source=:memory:").UseThinktectureValueConverters();
}
