# CodoMetis.TypeKit.Testing

Test assertions for the `Option`, `Result` and `Patch<T>` of
[CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit). None of them has a `.Value`, so a
test reaches the content through these, and fails with what the outcome was instead.

```bash
dotnet add package CodoMetis.TypeKit.Testing   # the test project
```

```csharp
using CodoMetis.TypeKit.Testing;

Email email = Email.Create("ops@example.com").ShouldBeSuccess();   // the value, for the test's own assertions
Email.Create("nobody").ShouldBeError(EmailFault.NoAt);              // this error, or the test fails
EmailFault fault = Email.Create("").ShouldBeError();                // any error, handed back

Email.TryFrom("ops@example.com").ShouldBeSome();
Email.TryFrom("nobody").ShouldBeNone();
```

A `Patch<T>` field of a request says whether the request mentioned it, and the assertions say the
same; `OrderPatch` here has `Patch<string> Name`, `Patch<string?> Nickname` and `Patch<int> Quantity`:

```csharp
OrderPatch patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;

int quantity = patch.Quantity.ShouldBeValue();   // the value, handed back
patch.Nickname.ShouldBeNull();                   // a clear
patch.Name.ShouldBeAbsent();                     // not in the document
```

- **The content comes back.** `ShouldBeSome()`, `ShouldBeSuccess()`, `ShouldBeError()` and
  `ShouldBeValue()` return the value or the error, so it is compared with whatever assertions the test
  already uses: xUnit's `Assert`, Shouldly, or another.
- **No test framework.** A failure is a `TypeKitAssertionException`, and every framework reports an
  exception from a test as its failure. The package depends on `CodoMetis.TypeKit` alone.
- **A failure says what the outcome was**: `Expected a success, but the Result<Email, EmailFault> is
  the error NoAt.` It names the error, which is what a test needs to see, and never prints a value, as
  `Option`, `Result` and `Patch<T>` themselves never do.
- **An uninitialized result fails every assertion**, saying it is a default instance, neither a
  success nor an error.
