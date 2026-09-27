#!/usr/bin/env bash
#
# End-to-end check of the path a consumer of the *packages* takes, which nothing else in the suite
# covers.
#
# Every other layer references the projects directly, and a ProjectReference hides exactly what
# breaks for a package consumer: a nuspec that forgets a dependency, an analyzer that never reaches
# the consumer's compiler, a fabric that weaves in this repository and not in a project that gets
# the generators transitively, a satellite that works through a project reference and not through
# the assembly a consumer restores. Each of those packs cleanly and restores cleanly.
#
# So this packs the packages, restores them into throwaway projects created *outside* the
# repository (inside it, Directory.Build.props would apply and they would stop resembling anything a
# consumer builds), compiles real code against them, runs it, and asserts on what it prints, on the
# diagnostics the build reports, and on the resolved package graph. Never on the exit code alone.
#
# Four consumers:
#   core     references only CodoMetis.TypeKit: Option and Result work, no Metalama arrives, and
#            CMTK0001 and CMTK0002 fire (docs/plan.md §10).
#   layered  a domain library references CodoMetis.TypeKit.Generators, and an app reaches everything
#            only through that library: the app uses the generated members, the transitive fabric
#            generates a value object the app declares itself, and CMTK0001 fires in the app.
#   host     a web host with both satellites: EF Core maps the value objects and translates .Value,
#            and the OpenAPI document describes them.
#   aot      the same ground published with Native AOT (docs/plan.md §11): no trim or AOT warning
#            from a package or from the code woven into the consumer, and the native binary does
#            what the JIT build does.
#
# Native AOT needs the platform linker: clang (and zlib on Linux), which the workflows install.
#
# Usage: consumer-smoke-test.sh [feed-directory]
#   With no argument the packages are packed fresh. Pass a directory of existing .nupkg files (the
#   release workflow passes its pack output) to test exactly the artifacts being shipped.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

feed="${1:-}"
failed=0

# A private package cache, and not an optimisation to skip.
#
# NuGet resolves id+version from the global packages folder before it looks at any source, so with
# the version held at the next release a locally built package is shadowed by whatever that version
# was restored as before, including one published on nuget.org. The test then reports on a package
# that has nothing to do with the working tree. A sibling repository's smoke test passed a
# deliberately sabotaged build that way.
export NUGET_PACKAGES="$work/packages"

version="$(dotnet msbuild "$repo_root/src/CodoMetis.TypeKit/CodoMetis.TypeKit.csproj" -getProperty:Version)"
version="$(echo "$version" | tr -d '[:space:]')"

# Every shipping project is a package, discovered from src/ as the release workflow does, so a new
# package is held to "it is in the feed" without an edit here.
packages=()
for directory in "$repo_root"/src/*/; do
    id="$(basename "$directory")"
    [[ -f "$directory$id.csproj" ]] && packages+=("$id")
done

if [[ -z "$feed" ]]; then
    feed="$work/feed"
    echo "==> Packing $version"
    dotnet build "$repo_root/CodoMetis.TypeKit.slnx" -c Release --verbosity quiet
    dotnet pack "$repo_root/CodoMetis.TypeKit.slnx" -c Release --no-build -o "$feed" --verbosity quiet
else
    feed="$(cd "$feed" && pwd)"
    echo "==> Using existing feed: $feed"
fi

for id in "${packages[@]}"; do
    if [[ ! -f "$feed/$id.$version.nupkg" ]]; then
        echo "FAILED: $id.$version.nupkg is not in $feed"
        exit 1
    fi
done
echo "    ${#packages[@]} packages: ${packages[*]}"

# Quiet while it works, but never silent when it does not: swallowing this output once hid an
# NU1101 behind a bare "exit code 1" in a sibling repository's CI.
run() {
    if ! output="$("$@" 2>&1)"; then
        echo "FAILED: $*"
        echo "$output" | sed 's/^/    /'
        exit 1
    fi
}

# For the builds that must fail: the output is kept for the assertions, and a build that succeeds
# is itself the failure being tested for.
run_failing() {
    if output="$("$@" 2>&1)"; then
        echo "FAILED: expected this to fail, and it succeeded: $*"
        echo "$output" | sed 's/^/    /'
        exit 1
    fi
}

assert_contains() {
    if grep -qF -- "$1" "$2"; then
        echo "  ok: $3"
    else
        echo "  FAIL: $3 (expected '$1' in $(basename "$2"))"
        sed 's/^/      /' "$2" | head -40
        failed=1
    fi
}

assert_not_contains() {
    if grep -qiF -- "$1" "$2"; then
        echo "  FAIL: $3 (did not expect '$1' in $(basename "$2"))"
        grep -iF -- "$1" "$2" | sed 's/^/      /' | head -10
        failed=1
    else
        echo "  ok: $3"
    fi
}

# Every CodoMetis.TypeKit package in the graph resolved at the version just packed.
assert_versions() {
    local listing="$1"
    if grep -F "> CodoMetis.TypeKit" "$listing" | grep -qvF " $version"; then
        echo "  FAIL: a CodoMetis.TypeKit package resolved at a version other than $version"
        grep -F "> CodoMetis.TypeKit" "$listing" | sed 's/^/      /'
        failed=1
    else
        echo "  ok: every CodoMetis.TypeKit package resolved at $version"
    fi
}

# One configuration for every consumer, at the top of the work directory, where NuGet and the SDK
# find it from each project below.
#
# Source mapping, not just source ordering: this repository's ids must come from the local feed and
# nowhere else, because a version on nuget.org satisfying the restore would mean testing something
# unrelated to the working tree. Everything else (Metalama, EF Core, ASP.NET) has to come from
# nuget.org, so restricting the whole restore to the local feed is not an option either: that fails
# with NU1101 on the transitive dependencies. <clear/> drops any machine-level sources.
#
# The repository's global.json comes along, so the consumers build on the SDK band the packages were
# built and tested with, also on a CI runner that carries several SDKs.
cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="CodoMetis.TypeKit*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML
cp "$repo_root/global.json" "$work/global.json"

# Creates a project from a template under $work and installs packages into it. No --source flag on
# `dotnet add package`: it would override the mapping above and restrict the whole restore,
# transitive dependencies included, to the local feed.
new_project() {
    local template="$1" path="$2"
    shift 2
    mkdir -p "$work/$path"
    cd "$work/$path"
    run dotnet new "$template" --framework net10.0 --output .
    rm -f Class1.cs
    for package in "$@"; do
        run dotnet add package "$package" --version "$version"
    done
}

list_packages() {
    run dotnet list package --include-transitive
    echo "$output" > "$1"
}

# ── core: the base package alone ─────────────────────────────────────────────────────────────────

echo "==> core: only CodoMetis.TypeKit"
new_project console core CodoMetis.TypeKit

cat > Program.cs <<'CSHARP'
using CodoMetis.TypeKit;

Option<int> some = Option.Some(3);
Option<int> none = Option.None();
Console.WriteLine($"some={some.Match(v => $"Some {v}", () => "None")}");
Console.WriteLine($"none={none.Match(v => $"Some {v}", () => "None")}");
Console.WriteLine($"printed=[{Option.Some(424242)}]");

Result<int, string> ok = 5;
Result<int, string> refused = Result.Error("nope");
Console.WriteLine($"ok={ok.Match(v => $"Ok {v}", e => $"Error {e}")}");
Console.WriteLine($"refused={refused.Match(v => $"Ok {v}", e => $"Error {e}")}");

// The command shape the README shows: the success marker and a bare error, one conditional.
Result<string> Cancel(bool removed) => removed ? Result.Success() : "gone";
Console.WriteLine($"cancelled={Cancel(true).Match(() => "Ok", e => $"Error {e}")}/{Cancel(false).Match(() => "Ok", e => $"Error {e}")}");

// An array slot is the one place a default Result still comes from: it must not pick a branch.
var slots = new Result<int, string>[1];
var slot = slots[0];
try
{
    slot.Match(_ => "", _ => "");
    Console.WriteLine("uninitialized=picked a branch");
}
catch (InvalidOperationException)
{
    Console.WriteLine("uninitialized=throws");
}
CSHARP

run dotnet build
list_packages "$work/core.packages"
run dotnet run --no-build --no-launch-profile
echo "$output" > "$work/core.out"
sed 's/^/      /' "$work/core.out"

echo "==> core: asserting"
assert_contains "> CodoMetis.TypeKit "           "$work/core.packages" "the base package resolved from the feed"
assert_contains "> CodoMetis.TypeKit.Analyzers " "$work/core.packages" "the analyzer package arrived with it"
assert_not_contains "Metalama"                   "$work/core.packages" "no Metalama in the graph of a base-only consumer"
assert_versions "$work/core.packages"
assert_contains "some=Some 3"          "$work/core.out" "Option.Some matches its value"
assert_contains "none=None"            "$work/core.out" "Option.None matches none"
assert_not_contains "424242"           "$work/core.out" "an Option's ToString never prints its content"
assert_contains "ok=Ok 5"              "$work/core.out" "a bare value converts to a successful Result"
assert_contains "refused=Error nope"   "$work/core.out" "Result.Error converts to a failed Result"
assert_contains "uninitialized=throws" "$work/core.out" "a default Result throws rather than picking a branch"
assert_contains "cancelled=Ok/Error gone" "$work/core.out" "Result<TError> converts from the success marker and a bare error"

# The analyzers run in the consumer's compiler. Both rules at once: a default Option, and a value
# object declared in a project that has no generator to implement it.
cat > Guard.cs <<'CSHARP'
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

public static class Guard
{
    public static Option<int> Nothing() => default(Option<int>);
}

public readonly partial record struct Ungenerated : IValue<Guid>;
CSHARP

run_failing dotnet build --no-incremental
echo "$output" > "$work/core.guard"
assert_contains "CMTK0001" "$work/core.guard" "CMTK0001 fires for default(Option<int>) in a base-only consumer"
assert_contains "CMTK0002" "$work/core.guard" "CMTK0002 fires for a value object with no generator"
rm Guard.cs

# ── layered: the generators in a domain library, an app that reaches them through it ─────────────

echo "==> layered: Domain references CodoMetis.TypeKit.Generators"
new_project classlib layered/Domain CodoMetis.TypeKit.Generators

cat > Shop.cs <<'CSHARP'
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace Shop;

public readonly partial record struct OrderId : IValue<Guid>;

public readonly partial record struct Quantity : IValue<int>;

public enum CodeFault { Blank, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);
        return new ProductCode(value);
    }
}
CSHARP

echo "==> layered: App references only Domain"
new_project console layered/App
run dotnet add reference ../Domain/Domain.csproj

cat > Program.cs <<'CSHARP'
using System.Text.Json;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;
using Shop;

var id = OrderId.New();
Console.WriteLine($"guid-version={id.Value.Version}");

var line = new Line(ProductCode.FromKnownGood("ABC"), Quantity.From(3));
Console.WriteLine($"json={JsonSerializer.Serialize(line)}");
Console.WriteLine($"tryfrom={ProductCode.TryFrom("abc").Match(_ => "accepted", () => "refused")}");

try
{
    JsonSerializer.Deserialize<Line>("""{"Code":"abc","Quantity":1}""");
    Console.WriteLine("json-read=accepted");
}
catch (JsonException refusal)
{
    Console.WriteLine($"json-read=refused, names the rule: {refusal.Message.Contains(nameof(CodeFault.NotUpperCase))}");
}

Console.WriteLine($"parsed={Quantity.Parse("42", null).Value}");

// Declared here, in a project that reaches the generators only through Domain: the transitive
// fabric has to generate it, or From does not exist and this does not compile.
Console.WriteLine($"local={Sku.From("X-1").Value}");

public sealed record Line(ProductCode Code, Quantity Quantity);

public readonly partial record struct Sku : IValue<string>;
CSHARP

run dotnet build
list_packages "$work/layered.packages"
run dotnet run --no-build --no-launch-profile
echo "$output" > "$work/layered.out"
sed 's/^/      /' "$work/layered.out"

echo "==> layered: asserting"
assert_contains "> CodoMetis.TypeKit.Generators " "$work/layered.packages" "the generators reach the app through Domain"
assert_contains "> CodoMetis.TypeKit.Analyzers "  "$work/layered.packages" "so do the analyzers"
assert_versions "$work/layered.packages"
assert_contains "guid-version=7"                  "$work/layered.out" "OrderId.New() is a version 7 Guid"
assert_contains 'json={"Code":"ABC","Quantity":3}' "$work/layered.out" "value objects serialize as the value they wrap"
assert_contains "tryfrom=refused"                 "$work/layered.out" "TryFrom applies Create"
assert_contains "json-read=refused, names the rule: True" "$work/layered.out" "the generated JSON converter applies Create"
assert_contains "parsed=42"                       "$work/layered.out" "the generated Parse"
assert_contains "local=X-1"                       "$work/layered.out" "the transitive fabric generates a value object the app declares"

cat > Guard.cs <<'CSHARP'
using Shop;

public static class Guard
{
    public static OrderId Nothing() => default;
}
CSHARP

run_failing dotnet build --no-incremental
echo "$output" > "$work/layered.guard"
assert_contains "CMTK0001"     "$work/layered.guard" "CMTK0001 fires in a project that reaches the package only through another"
assert_not_contains "CMTK0002" "$work/layered.guard" "no CMTK0002 where the generators arrive transitively"
rm Guard.cs

# ── host: both satellites, from packages ─────────────────────────────────────────────────────────

echo "==> host: EF Core and OpenAPI satellites"
new_project web host CodoMetis.TypeKit.EntityFrameworkCore CodoMetis.TypeKit.AspNetCore
# Third-party packages at the versions the satellites are built against, read from central
# management. Microsoft.AspNetCore.OpenApi is referenced directly, as the package README says: it
# enables its source generator's interceptors in build/, which NuGet imports for a direct reference
# only, and a host that gets it only through the satellite fails with CS9137.
central_version() { sed -n "s/.*Include=\"$1\" Version=\"\([^\"]*\)\".*/\1/p" "$repo_root/Directory.Packages.props"; }
run dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version "$(central_version Microsoft.EntityFrameworkCore.Relational)"
run dotnet add package Microsoft.AspNetCore.OpenApi --version "$(central_version Microsoft.AspNetCore.OpenApi)"
run dotnet add reference ../layered/Domain/Domain.csproj

cat > Program.cs <<'CSHARP'
using System.Text.Json.Nodes;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shop;

// OpenAPI: a real host, its own document, and the binding behind it.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddOpenApi(options => options.AddTypeKit());

await using (var app = builder.Build())
{
    app.MapOpenApi();
    app.MapGet("/orders/{id}", (OrderId id) => new OrderView(id, [Quantity.From(1)]));
    await app.StartAsync();

    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
    // "missing" rather than a NullReferenceException, so a document without the node fails the
    // assertion that names it.
    var document = JsonNode.Parse(await http.GetStringAsync("/openapi/v1.json"))!;
    var schemas = document["components"]?["schemas"];
    Console.WriteLine($"openapi-component={schemas?["OrderId"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"openapi-parameter={document["paths"]?["/orders/{id}"]?["get"]?["parameters"]?[0]?["schema"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"openapi-items={schemas?["OrderView"]?["properties"]?["quantities"]?["items"]?.ToJsonString() ?? "missing"}");
    Console.WriteLine($"bound={(int)(await http.GetAsync($"/orders/{Guid.NewGuid()}")).StatusCode}");
    Console.WriteLine($"malformed={(int)(await http.GetAsync("/orders/nope")).StatusCode}");
    await app.StopAsync();
}

// EF Core: mapped with nothing registered per type, round-tripped, and .Value translated.
await using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var options = new DbContextOptionsBuilder<ShopDb>().UseSqlite(connection).UseTypeKit().Options;

await using (var db = new ShopDb(options))
{
    db.Database.EnsureCreated();
    db.Orders.Add(new Order { Id = OrderId.New(), Code = ProductCode.FromKnownGood("ABC"), Quantity = Quantity.From(2) });
    await db.SaveChangesAsync();
}

await using (var db = new ShopDb(options))
{
    var query = db.Orders.Where(o => o.Code.Value.StartsWith("AB"));
    Console.WriteLine($"sql={query.ToQueryString().ReplaceLineEndings(" ")}");
    var found = await query.SingleAsync();
    Console.WriteLine($"found={found.Code.Value}:{found.Quantity.Value}");
}

public sealed record OrderView(OrderId Id, List<Quantity> Quantities);

public sealed class Order
{
    public OrderId Id { get; set; }
    public ProductCode Code { get; set; }
    public Quantity Quantity { get; set; }
}

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
CSHARP

run dotnet build
list_packages "$work/host.packages"
run dotnet run --no-build --no-launch-profile
echo "$output" > "$work/host.out"
sed 's/^/      /' "$work/host.out"

echo "==> host: asserting"
assert_contains "> CodoMetis.TypeKit.EntityFrameworkCore " "$work/host.packages" "the EF Core satellite resolved from the feed"
assert_contains "> CodoMetis.TypeKit.AspNetCore "          "$work/host.packages" "the OpenAPI satellite resolved from the feed"
assert_versions "$work/host.packages"
assert_contains 'openapi-component={"type":"string","format":"uuid"}' "$work/host.out" "OrderId is a uuid component in the document"
assert_contains 'openapi-parameter={"type":"string","format":"uuid"}' "$work/host.out" "the route parameter is a uuid"
assert_contains 'openapi-items={"$ref":"#/components/schemas/Quantity"}' "$work/host.out" "a list of value objects keeps its items"
assert_contains "bound=200"      "$work/host.out" "a route parameter binds through the generated TryParse"
assert_contains "malformed=400"  "$work/host.out" "a malformed route parameter is a 400"
assert_contains '"o"."Code" LIKE' "$work/host.out" ".Value in a query translates to the bare column"
assert_contains "found=ABC:2"    "$work/host.out" "value objects round-trip through the database"

# ── aot: the packages under Native AOT ───────────────────────────────────────────────────────────
#
# The packages build with the trim and AOT analyzers on, but those cannot see the code the
# generators weave into a consumer, and they cannot tell that a run-time path still works. Under
# Native AOT an interface nothing uses is removed from a type, and reflection over it finds nothing:
# that is how the OpenAPI satellite once described every value object as {} with no warning at any
# build (docs/plan.md §11). So this publishes a consumer with PublishAot, reads the publish warnings,
# and runs the native binary.
#
# The value objects live in namespace Shop, so a warning about woven code names Shop.; a warning in
# a package names CodoMetis. TrimmerSingleWarn=false keeps the packages' warnings itemised instead of
# folded into one line per assembly. EF Core's own warnings (its DbContext constructor requires
# dynamic code) name neither and are EF's to fix.

echo "==> aot: the packages published with Native AOT"
command -v clang >/dev/null || { echo "FAILED: Native AOT needs clang on PATH (and zlib on Linux)"; exit 1; }

new_project classlib aot/Domain CodoMetis.TypeKit.Generators
run dotnet add package NodaTime --version "$(central_version NodaTime)"

cat > Shop.cs <<'CSHARP'
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.ValueObjects;

namespace Shop;

// One per JSON strategy: a reader method (Guid), the serializer's contract for a number, the
// fallback with a built-in converter (Uri, an enum), a string, and parsing through a TypeConverter.
public readonly partial record struct OrderId : IValue<Guid>;
public readonly partial record struct Quantity : IValue<int>;
public readonly partial record struct Amount : IValue<decimal>;
public readonly partial record struct Link : IValue<Uri>;
public readonly partial record struct Weekday : IValue<DayOfWeek>;
public readonly partial record struct Name : IValue<string>;
public readonly partial record struct Day : IValue<NodaTime.LocalDate>;

public enum CodeFault { Blank, NotUpperCase }

// A record class: EF's compiled model cannot write the sentinel of a struct that wraps a reference
// type (its default converts to null), so a string-backed value object on an entity is a class here.
public sealed partial record Code : IValidatedValue<Code, string, CodeFault>
{
    public static Result<Code, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);
        return new Code(value);
    }
}
CSHARP

# Two applications: JSON and OpenAPI in a web app, EF Core in a console app of its own, the way its
# compiled model is generated.
#
# What a run cannot see: anything that uses IValueObject<,> (the EF satellite's converter, or the
# generated [GeneratedValueObject<,>] through its constraint) makes the trimmer keep that interface
# on every value object, so a GetInterfaces() lookup passes here as well (measured). Before the
# attribute existed it found nothing, and the OpenAPI assertions below failed. That lookup is kept
# out by the analyzers, whose IL2070 fails the packages' build, and by AGENTS.md.
aot_project() {
    local template="$1" path="$2"
    shift 2
    new_project "$template" "$path" "$@"
    run dotnet add reference ../Domain/Domain.csproj
    # PublishAot, the interceptors EF's precompiled queries are, and itemised package warnings.
    sed -i.bak 's|</PropertyGroup>|  <PublishAot>true</PublishAot>\n    <TrimmerSingleWarn>false</TrimmerSingleWarn>\n    <InterceptorsNamespaces>$(InterceptorsNamespaces);Microsoft.EntityFrameworkCore.GeneratedInterceptors</InterceptorsNamespaces>\n  </PropertyGroup>|' "$(basename "$path").csproj"
    rm "$(basename "$path").csproj.bak"
}

# Publishes, keeps the warnings that name a package or a woven value object, runs the native binary.
aot_publish_and_run() {
    local name="$1"
    run dotnet publish -c Release -o "$work/aot/$name.out"
    echo "$output" > "$work/aot.$name.publish"
    grep -E 'warning IL[0-9]{4}' "$work/aot.$name.publish" | grep -E 'CodoMetis|Shop\.' | sort -u >> "$work/aot.warnings" || true
    if [[ -x "$work/aot/$name.out/$name" && ! -f "$work/aot/$name.out/$name.dll" ]]; then
        echo "  ok: $name is published as native code"
    else
        echo "  FAIL: $name was not published as a native executable"
        failed=1
    fi
    run "$work/aot/$name.out/$name"
    echo "$output" >> "$work/aot.out"
}

: > "$work/aot.warnings"
: > "$work/aot.out"

# JSON and OpenAPI.
aot_project web aot/Web CodoMetis.TypeKit.AspNetCore
run dotnet add package Microsoft.AspNetCore.OpenApi --version "$(central_version Microsoft.AspNetCore.OpenApi)"

cat > Program.cs <<'CSHARP'
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.AspNetCore;
using CodoMetis.TypeKit.ValueObjects;
using Shop;

async Task Check(string name, Func<Task<string>> body)
{
    try { Console.WriteLine($"{name}={await body()}"); }
    catch (Exception e) { Console.WriteLine($"{name}=THREW {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}"); }
}

// JSON through a source-generated context, the only resolver under Native AOT, which lists none of
// the wrapped types: the generated converters must not need them.
var line = new Line(OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee")), Code.FromKnownGood("ABC"), Name.From("n"), Quantity.From(3),
    Amount.From(1.5m), Link.From(new Uri("https://x.test/a")), Weekday.From(DayOfWeek.Friday),
    new() { [Quantity.From(7)] = 1 }, new() { [Weekday.From(DayOfWeek.Monday)] = 2 });
var written = "";
await Check("json", async () => written = JsonSerializer.Serialize(line, ShopJson.Default.Line));
await Check("json-roundtrip", async () => (JsonSerializer.Serialize(JsonSerializer.Deserialize(written, ShopJson.Default.Line)!, ShopJson.Default.Line) == written).ToString());
await Check("json-refused", async () => { JsonSerializer.Deserialize(written.Replace("\"ABC\"", "\"abc\""), ShopJson.Default.Line); return "accepted"; });
await Check("json-number-handling", async () =>
{
    var options = new JsonSerializerOptions(ShopJson.Default.Options) { NumberHandling = JsonNumberHandling.WriteAsString };
    return JsonSerializer.Serialize(Amount.From(2.5m), options);
});
await Check("stored", async () =>
{
    var store = new JsonSerializerOptions { TypeInfoResolver = ShopJson.Default, Converters = { new StoredJsonConverterFactory() } };
    return JsonSerializer.Deserialize<Code>("\"abc\"", store)!.Value;
});
await Check("option-refused", async () => JsonSerializer.Serialize(new Holder(Option.Some(1)), ShopJson.Default.Holder));

// Parsing, including through a TypeConverter (NodaTime's LocalDate).
await Check("parse", async () => $"{Quantity.Parse("42", null).Value} {Weekday.Parse("Monday", null).Value} {Link.Parse("https://p.test/", null).Value} {Day.Parse("2026-09-27", null).Value:yyyy-MM-dd}");
await Check("tryparse-refused", async () => Code.TryParse("abc", null, out _).ToString());

// OpenAPI. Under source-generated JSON the host's context lists what the value objects wrap, since
// the wrapped type's schema is ASP.NET's own, built from the host's JSON contract for it.
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, HostJson.Default));
builder.Services.AddOpenApi(options => options.AddTypeKit());

await using var app = builder.Build();
app.MapOpenApi();
app.MapGet("/orders/{id}", (OrderId id, Quantity? limit) => new OrderView(id, [Quantity.From(1)], Amount.From(1.5m)));
app.MapPost("/orders", (OrderView body) => body.Amount);
await app.StartAsync();

using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
JsonNode? document = null;
await Check("openapi", async () =>
{
    var response = await http.GetAsync("/openapi/v1.json");
    document = JsonNode.Parse(await response.Content.ReadAsStringAsync());
    return ((int)response.StatusCode).ToString();
});
var schemas = document?["components"]?["schemas"];
Console.WriteLine($"openapi-component={schemas?["OrderId"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-parameter={document?["paths"]?["/orders/{id}"]?["get"]?["parameters"]?[0]?["schema"]?.ToJsonString() ?? "missing"}");
Console.WriteLine($"openapi-items={schemas?["OrderView"]?["properties"]?["quantities"]?["items"]?.ToJsonString() ?? "missing"}");
await Check("bound", async () => ((int)(await http.GetAsync($"/orders/{Guid.NewGuid()}?limit=3")).StatusCode).ToString());
await Check("malformed", async () => ((int)(await http.GetAsync("/orders/nope")).StatusCode).ToString());
await app.StopAsync();

public sealed record Line(OrderId Id, Code Code, Name Name, Quantity Quantity, Amount Amount, Link Link, Weekday Weekday,
    Dictionary<Quantity, int> ByQuantity, Dictionary<Weekday, int> ByDay);

public sealed record OrderView(OrderId Id, List<Quantity> Quantities, Amount Amount);

public sealed record Holder(Option<int> Value);

// Lists no wrapped type: the generated converters must not need one.
[JsonSerializable(typeof(Line))]
[JsonSerializable(typeof(Holder))]
[JsonSerializable(typeof(Code))]
[JsonSerializable(typeof(Amount))]
internal partial class ShopJson : JsonSerializerContext;

// The web host's: what the endpoints exchange, and what their value objects wrap, for the schemas.
[JsonSerializable(typeof(OrderView))]
[JsonSerializable(typeof(Amount))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(decimal))]
internal partial class HostJson : JsonSerializerContext;
CSHARP

list_packages "$work/aot.packages"
aot_publish_and_run Web

# EF Core: a compiled model and precompiled queries, generated before publishing, which is how EF
# runs under Native AOT at all. The id query maps its value-object parameter at run time.
aot_project console aot/Data CodoMetis.TypeKit.EntityFrameworkCore
ef_version="$(central_version Microsoft.EntityFrameworkCore.Relational)"
run dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version "$ef_version"
run dotnet add package Microsoft.EntityFrameworkCore.Design --version "$ef_version"

cat > Program.cs <<'CSHARP'
using CodoMetis.TypeKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shop;

async Task Check(string name, Func<Task<string>> body)
{
    try { Console.WriteLine($"{name}={await body()}"); }
    catch (Exception e) { Console.WriteLine($"{name}=THREW {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}"); }
}

var id = OrderId.From(Guid.Parse("0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee"));

await using var db = new StoreDb();
await db.Database.OpenConnectionAsync();
await db.Database.ExecuteSqlRawAsync("CREATE TABLE Orders (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL, Quantity INTEGER NOT NULL)");
await Check("ef-insert", async () =>
{
    db.Orders.Add(new Order { Id = id, Code = Code.FromKnownGood("ABC"), Quantity = Quantity.From(2) });
    return (await db.SaveChangesAsync()).ToString();
});
db.ChangeTracker.Clear();
await Check("ef-value", async () => (await db.Orders.Where(o => o.Code.Value.StartsWith("AB")).SingleAsync()).Quantity.Value.ToString());
db.ChangeTracker.Clear();
await Check("ef-parameter", async () => (await db.Orders.Where(o => o.Id == id).SingleAsync()).Code.Value);
db.ChangeTracker.Clear();
await Check("ef-stored", async () =>
{
    await db.Database.ExecuteSqlRawAsync("UPDATE Orders SET Code = 'lower'");
    return (await db.Orders.Where(o => o.Id == id).SingleAsync()).Code.Value;
});

// Not sealed: EF 10's precompiled queries cast an entity to an internal interface, which does not
// compile for a sealed class.
public class Order
{
    public OrderId Id { get; set; }
    public Code Code { get; set; } = null!;
    public Quantity Quantity { get; set; }
}

public sealed class StoreDb : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=:memory:").UseTypeKit();
}
CSHARP

run dotnet new tool-manifest
run dotnet tool install dotnet-ef --version "$ef_version"
run dotnet ef dbcontext optimize --precompile-queries --nativeaot
list_packages "$work/aot.data.packages"
aot_publish_and_run Data
sed 's/^/      /' "$work/aot.out"

echo "==> aot: asserting"
assert_versions "$work/aot.packages"
assert_versions "$work/aot.data.packages"
if [[ -s "$work/aot.warnings" ]]; then
    echo "  FAIL: trim or AOT warnings from the packages or the woven code"
    sed 's/^/      /' "$work/aot.warnings" | head -20
    failed=1
else
    echo "  ok: no trim or AOT warning names a package or a woven value object"
fi
assert_contains 'json={"Id":"0199aaaa-bbbb-7ccc-8ddd-eeeeeeeeeeee","Code":"ABC","Name":"n","Quantity":3,"Amount":1.5,"Link":"https://x.test/a","Weekday":5,"ByQuantity":{"7":1},"ByDay":{"Monday":2}}' \
                 "$work/aot.out" "every JSON strategy writes through a context that lists no wrapped type"
assert_contains "json-roundtrip=True"              "$work/aot.out" "and reads back what it wrote"
assert_contains "json-refused=THREW JsonException: Code refused the JSON value (NotUpperCase)" "$work/aot.out" "the generated JSON converter applies Create"
assert_contains 'json-number-handling="2.5"'       "$work/aot.out" "the options' number handling applies to a wrapped number"
assert_contains "stored=abc"                       "$work/aot.out" "StoredJsonConverterFactory reads without the rules"
assert_contains "option-refused=THREW NotSupportedException: Option<Int32> is not a wire type" "$work/aot.out" "Option refuses JSON with its own message"
assert_contains "parse=42 Monday https://p.test/ 2026-09-27" "$work/aot.out" "the generated Parse, including through a TypeConverter"
assert_contains "tryparse-refused=False"           "$work/aot.out" "the generated TryParse applies Create"
assert_contains "openapi=200"                      "$work/aot.out" "the OpenAPI document is generated"
assert_contains 'openapi-component={"type":"string","format":"uuid"}' "$work/aot.out" "OrderId is a uuid component"
assert_contains 'openapi-parameter={"type":"string","format":"uuid"}' "$work/aot.out" "the route parameter is a uuid"
assert_contains 'openapi-items={"$ref":"#/components/schemas/Quantity"}' "$work/aot.out" "a list of value objects keeps its items"
assert_contains "bound=200"                        "$work/aot.out" "a route parameter binds through the generated TryParse"
assert_contains "malformed=400"                    "$work/aot.out" "a malformed route parameter is a 400"
assert_contains "ef-insert=1"                      "$work/aot.out" "EF Core writes value objects through the compiled model"
assert_contains "ef-value=2"                       "$work/aot.out" ".Value translates in a precompiled query"
assert_contains "ef-parameter=ABC"                 "$work/aot.out" "a value-object parameter is mapped at run time"
assert_contains "ef-stored=lower"                  "$work/aot.out" "a stored value the rules refuse is read without them"

[[ $failed -eq 0 ]] || { echo; echo "consumer smoke test FAILED"; exit 1; }
echo
echo "consumer smoke test passed ($version)"
