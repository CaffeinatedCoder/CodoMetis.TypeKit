# Testing

This page shows the tests you typically write for value objects, `Option` and `Result`: assertions
that hand you the content, `Create` as a table, the generated members checked against `Create`, EF
Core on SQLite, and an endpoint on the test server.

`Option` and `Result` have no `.Value`, so your tests reach the content through
`CodoMetis.TypeKit.Testing`. Its assertions hand the content back, and when they fail, they say what
the outcome was. The [Testing README](../src/CodoMetis.TypeKit.Testing/README.md) is the package's
reference.

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

## Assert on an Option or a Result

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

The assertions work beside whatever your tests already use: xUnit's `Assert`, Shouldly, or anything
else. A failure throws a `TypeKitAssertionException`, and every test framework reports that as the
test's failure. That is why the package depends on nothing but `CodoMetis.TypeKit`.

A failure prints the error through its `ToString()`, which is what you need to see in a test. It
never prints a value, just as `Option` and `Result` never do. An uninitialized result fails every
assertion, and the message says it is a default instance.

A request's `Patch<T>` fields tell you which of them the request mentioned, and the assertions check
the same three cases:

- **`ShouldBeValue()`** hands the value back.
- **`ShouldBeNull()`** checks for a clear.
- **`ShouldBeAbsent()`** checks for a field the document left out.

```csharp
public sealed record CodePatch(Patch<ProductCode> Code, Patch<string> Note, Patch<int> Quantity);
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

## Test Create as a table

A value object's rules are its contract. A table puts all of them in one place:

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

## Check the generated members against Create

Test what the generators derive from `Create` by how it behaves, not by whether it exists. The build
already proves that `TryParse` exists. Only a test proves that it rejects what `Create` rejects.

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

## Test EF Core on SQLite

Open one `SqliteConnection` to `:memory:`, call `UseTypeKit()` on the options, and create the schema
with `EnsureCreated`. [EF Core](ef-core.md#keys) has the whole block, and
[SQLite in tests](ef-core.md#sqlite-in-tests) explains why EF's in-memory provider can't stand in.

## Test an endpoint on the test server

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

The body went through the generated JSON converter, so the 400 is `Create` rejecting the code. What
the response says, in a minimal API and in MVC, is in
[ASP.NET Core](aspnet-core.md#what-a-refused-value-answers).

## Analyzers in your tests

The analyzers run in test projects as they do everywhere else.

- **`default(ProductCode)` fails the build (CMTK0001)**, in a test too, as it should. When a test
  needs a default instance, say so with a pragma around the line. This repository's own tests do the
  same.
- **Comparing with `default` is fine.** It is a guard, so `id.ShouldNotBe(default)` isn't reported.
  Neither is a `default` assigned to the `out` parameter of a `Try` method.
- **A dropped `Result` is reported (CMTK0003)**, in a test too. Write `_ = ProductCode.Create(input);`
  to show the drop is meant.
