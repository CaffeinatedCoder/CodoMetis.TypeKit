# Testing

`Option` and `Result` have no `.Value`, so a test reaches the content through
`CodoMetis.TypeKit.Testing`, which hands it back and fails saying what the outcome was. The rest of
this page is the shape of the tests a codebase on value objects ends up writing: `Create` as a table,
the generated ways in against `Create`, EF Core on SQLite, and an endpoint on the test server. The
[Testing README](../src/CodoMetis.TypeKit.Testing/README.md) is the package's reference.

```csharp
using CodoMetis.TypeKit;
using CodoMetis.TypeKit.Testing;
using CodoMetis.TypeKit.ValueObjects;
```

```csharp
public enum CodeFault { Blank, NotUpperCase }

public readonly partial record struct ProductCode : IValidatedValue<ProductCode, string, CodeFault>
{
    public static Result<ProductCode, CodeFault> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Result.Error(CodeFault.Blank);
        if (value != value.ToUpperInvariant()) return Result.Error(CodeFault.NotUpperCase);

        return new ProductCode(value.Trim());
    }
}
```

## The assertions

```csharp
[Fact]
public void A_code_is_created_or_refused()
{
    ProductCode code = ProductCode.Create("ABC").ShouldBeSuccess();   // the value, for the test's own assertions
    code.Value.ShouldBe("ABC");

    ProductCode.Create("abc").ShouldBeError(CodeFault.NotUpperCase);   // this error, or the test fails
    CodeFault fault = ProductCode.Create(" ").ShouldBeError();          // any error, handed back
    fault.ShouldBe(CodeFault.Blank);

    ProductCode.TryFrom("ABC").ShouldBeSome().ShouldBe(code);
    ProductCode.TryFrom("abc").ShouldBeNone();
}
```

```csharp
string failure = Result.Catch(() => ProductCode.Create("abc").ShouldBeSuccess()).Match(_ => "", error => error.Message);   // Expected a success, but the Result<ProductCode, CodeFault> is the error NotUpperCase.
```

A failure is a `TypeKitAssertionException`, which every framework reports as the test's failure, so
the package depends on nothing but `CodoMetis.TypeKit`; the assertions sit beside xUnit's `Assert`,
Shouldly or whatever the test already uses. A failure names the error, which is what a test needs
to see, and never prints a value, as `Option` and `Result` never do. An uninitialized result fails
every assertion, saying it is a default instance.

A request's `Patch<T>` fields say which of them the request mentioned, and the assertions say the
same: `ShouldBeValue()` hands the value back, `ShouldBeNull()` is a clear, and `ShouldBeAbsent()` a
field the document left out.

```csharp
public sealed record CodePatch(Patch<ProductCode> Code, Patch<string?> Note, Patch<int> Quantity);
```

```csharp
[Fact]
public void A_patch_says_what_the_request_mentioned()
{
    CodePatch patch = JsonSerializer.Deserialize<CodePatch>("""{"note":null,"quantity":2}""", JsonSerializerOptions.Web)!;

    patch.Quantity.ShouldBeValue().ShouldBe(2);   // the value, for the test's own assertions
    patch.Note.ShouldBeNull();                    // a clear
    patch.Code.ShouldBeAbsent();                  // not in the document
}
```

## Create as a table

The rules are the value object's contract, and a table says all of them in one place:

```csharp
[Theory]
[InlineData("ABC", null)]
[InlineData(" ABC ", null)]
[InlineData("", CodeFault.Blank)]
[InlineData("   ", CodeFault.Blank)]
[InlineData("abc", CodeFault.NotUpperCase)]
public void Create_applies_each_rule(string input, CodeFault? refused)
{
    var created = ProductCode.Create(input);

    if (refused is { } fault) created.ShouldBeError(fault);
    else created.ShouldBeSuccess().Value.ShouldBe(input.Trim());
}
```

## The generated ways in agree with Create

What the generators derive from `Create` is tested through behaviour, not presence: a build proves
`TryParse` exists, and only a test proves it refuses what `Create` refuses.

```csharp
[Fact]
public void Every_way_in_applies_the_rules()
{
    ProductCode.TryParse("abc", null, out _).ShouldBeFalse();
    Should.Throw<FormatException>(() => ProductCode.Parse("abc", null)).Message.ShouldContain("NotUpperCase");
    Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ProductCode>("\"abc\"")).Message.ShouldContain("NotUpperCase");
    Should.Throw<InvalidOperationException>(() => ProductCode.FromKnownGood("abc")).Message.ShouldContain("NotUpperCase");
    ProductCode.FromKnownGood("ABC").Revalidate().ShouldBeSuccess();
}
```

## EF Core on SQLite

One open `SqliteConnection` to `:memory:`, `UseTypeKit()` on the options and `EnsureCreated` for
the schema; [EF Core](ef-core.md#keys) has the whole block, and [SQLite in tests](ef-core.md#sqlite-in-tests)
says why EF's in-memory provider cannot stand in.

## An endpoint on the test server

```csharp
public sealed record CodeRequest(ProductCode Code);
```

```csharp
[Fact]
public async Task A_refused_body_is_a_400()
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
    builder.WebHost.UseTestServer();
    builder.Logging.ClearProviders();
    await using var app = builder.Build();
    app.MapPost("/codes", (CodeRequest request) => request.Code.Value);
    await app.StartAsync(TestContext.Current.CancellationToken);

    var client = app.GetTestClient();
    var accepted = await client.PostAsJsonAsync("/codes", new { code = "ABC" }, TestContext.Current.CancellationToken);
    var refused = await client.PostAsJsonAsync("/codes", new { code = "abc" }, TestContext.Current.CancellationToken);

    accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
}
```

The body went through the generated JSON converter, so the 400 is `Create`'s refusal; what the
response says, in a minimal API and in MVC, is in [ASP.NET Core](aspnet-core.md#what-a-refused-value-answers).

## The analyzers in a test project

The analyzers run in test projects as everywhere, so `default(ProductCode)` in a test is CMTK0001,
as it should be: a test that needs a default instance says so with a pragma around the line, which
is also what this repository's tests do. A comparison with `default` is a guard and is not reported
(`id.ShouldNotBe(default)`), nor is a `default` assigned to the `out` parameter of a `Try` method. A
dropped `Result` in a test is CMTK0003, so `_ = ProductCode.Create(input);` says the drop is meant.
