# CodoMetis.TypeKit.Testing

Test assertions for the `Option`, `Result` and `Patch<T>` of
[CodoMetis.TypeKit](https://www.nuget.org/packages/CodoMetis.TypeKit). None of these types has a
`.Value`, so your tests reach the content through these assertions. They hand the content back, and
when they fail, they say what the outcome was.

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

A request's `Patch<T>` field tells you whether the request mentioned it, and the assertions check
the same. In this example, `OrderPatch` has `Patch<string> Name`, `Patch<string> Nickname` and
`Patch<int> Quantity`:

```csharp
OrderPatch patch = JsonSerializer.Deserialize<OrderPatch>("""{"nickname":null,"quantity":2}""", JsonSerializerOptions.Web)!;

int quantity = patch.Quantity.ShouldBeValue();   // the value, handed back
patch.Nickname.ShouldBeNull();                   // a clear
patch.Name.ShouldBeAbsent();                     // not in the document
```

A fault of several types, such as `Fault<OrderFault, EditFault>`, has no public content either.
`ShouldHold(reason)` fails unless the fault holds that reason, whichever of its types the reason
belongs to. It hands back the `Fault<TFault>` with its detail and rule intact. A reason of a type the
fault isn't made of doesn't compile.

```csharp
Fault<OrderFault, EditFault> refusal = orders.Rename(7, version: 3).ShouldBeError();
Fault<EditFault> stale = refusal.ShouldHold(EditFault.Stale);   // this fault, or the test fails
string? detail = stale.Detail;                                  // for the test's own assertions
```

Every assertion also works on a `Task` or a `ValueTask` of what it checks. Put `await` in front, and
it waits for the task, then checks what the task returned:

```csharp
Email email = await signups.RegisterAsync("ops@example.com").ShouldBeSuccess();   // awaited, then asserted
await signups.RegisterAsync("nobody").ShouldBeError(EmailFault.NoAt);
await signups.FindAsync("ops@example.com").ShouldBeNone();                         // a ValueTask too
```

- **The content comes back.** `ShouldBeSome()`, `ShouldBeSuccess()`, `ShouldBeError()` and
  `ShouldBeValue()` return the value or the error. Compare it with whatever assertions your tests
  already use: xUnit's `Assert`, Shouldly, or another.
- **No test framework needed.** A failure throws a `TypeKitAssertionException`, and every framework
  reports an exception from a test as its failure. The package depends on `CodoMetis.TypeKit` alone.
- **A failure says what the outcome was**: `Expected a success, but the Result<Email, EmailFault> is
  the error NoAt.` It prints the error through its `ToString()`, which is what you need to see in a
  test. So an exception's message or a `Fault`'s detail shows up. It never prints a value, just as
  `Option`, `Result` and `Patch<T>` themselves never do.
- **An uninitialized result fails every assertion.** The message says it is a default instance,
  neither a success nor an error.
- **Await an assertion on a task.** It fails your test only through the task it returns, so without
  `await` the test passes whatever the task holds. The analyzer reports that line (CMTK0003), in a
  test method that isn't `async` too, where C# itself says nothing.
- **A task that throws fails your test with its own exception**, as it was thrown, and not as an
  assertion failure. The assertion never sees an outcome.
