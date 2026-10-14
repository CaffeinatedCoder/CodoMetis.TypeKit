# Option and Result

`Result<T,TError>`, `Result<TError>` and `Option<T>`, with the `Option.None()`, `Result.Success()` and
`Result.Error()` markers ([decision 19](adr/0019-concise-implicit.md)), ship in `CodoMetis.TypeKit`. The value-object contracts
depend on them: `IValidatedValue.Create` returns a `Result`, and the generated `TryFrom` returns an
`Option`.

**Non-goals.** No Either, no Validation applicative, no effect or IO types, no higher-kinded type
emulation, no immutable collections. No `Try` type: it is `Result<T, Exception>` under another name,
and an exception enters through `Result.Catch` (phase 13, [decision 43](adr/0043-exceptions-type-argument-constraints-constructor-guarantee.md)). No `Unit`: `Result<TError>`
is the value-less shape. Anything that turns this into a second LanguageExt is out.

**Shape, decided 2026-09-27.**
- **`Result<T, TError>`, value first.** Error-first (`Either e a`) exists in Haskell and Scala
  only because a generic type there can be filled in from the left alone, which C# cannot do.
  Rust, F#, Swift, DotNext, CSharpFunctionalExtensions and the csharplang unions design note all
  put the value first. `IValidatedValue<TSelf, T, TFault>` does too.
- **`readonly record struct`, non-positional, private fields.** The synthesized equality is
  correct (`State` included). The synthesized `ToString` prints only public members:
  `Option { }` and `Result { State = Error }`. So it never prints a value, in line with
  `FromKnownGood`'s message. **Never make these types positional:** positional parameters become public
  properties, which reintroduces `.Value` and puts the value into `ToString`.
- **The markers** ([decision 19](adr/0019-concise-implicit.md)): `Option.None()`, `Result.Success()`, `Result.Success(x)` and
  `Result.Error(e)` return a small struct that converts implicitly to whatever `Option`/`Result`
  the target is, so a method returns one without spelling out type arguments. They keep their
  content internal too, so they print `Success { }` and `Error { }`. Only the implicit conversions
  read it. Both shapes accept both result markers: `Result<TError>` converts from `Result.Error(e)`
  as well as from a bare error, so a method can `return Result.Error(fault);` whichever shape it
  returns. No combinator takes a marker-returning lambda: `Bind(x => Result.Success(x))` was `Map`
  under another name, and is a compile error now.
- **`Result<T, TError>` converts from a bare value, never from a bare error** (measured
  2026-09-27). With both conversions, `Result<long, int> r = 5;` compiles and is an **error**:
  `int` is the more specific source type, so C# picks the error conversion. The READMEs had shown
  `return Fault.X;` in valued methods, which does not compile; the consumer smoke test found it.
- **`Option.Some(null)` throws.** `notnull` is an annotation the runtime does not enforce, and a
  `Some` over null reported a value it could not hand out. `Map` and the zips go through `Some`,
  so a selector that returns null throws too.
- **So do `Result`'s factories.** Both shapes and both markers constrain their content to
  `notnull`, and `Success(null)`/`Error(null)`/`Result.Success(null)`/`Result.Error(null)` throw
  `ArgumentNullException`: a result over null handed it out of `TryGetValue`/`TryGetError` despite
  `[NotNullWhen]`. `Map`, `MapError`, `Bind` and the conversions go through them.
- **Every delegate is checked before a branch is picked**, in `Option`, both `Result` shapes and
  their extensions: a null for the branch not taken passed until the other outcome first arrived.
  `NullDelegateTests` holds every delegate parameter of the assembly to a case run on both branches.
- **`Result<TError>.Match` hands the error to its error branch.** The only overload took a
  parameterless `onError`, so `TryGetError` was the only way to the error. The parameterless one
  stays, for a branch that does not need it.
- **`[DebuggerDisplay]`** on `Option` and both `Result` shapes shows the content in the debugger,
  where it is what someone stepping through wants to see. The debugger is not a log.
- **`Result<TError>` has no `AsEnumerable`.** A sequence of zero or one units says no more than
  the `bool` conversion.
- **`Fault<TFault>` (2.0.0)** carries a reason from a fixed set and an optional sentence about one
  occurrence. It is made by `Fault.Of` alone, and comparing it with a bare reason is refused, since a
  fault with a detail never equals one
  ([decision 53](adr/0053-fault-made-by-fault-of-alone.md)); `Reason` is what a caller compares.
  A `Cause` beside them joins 2.1 ([decision 52](adr/0052-cause-on-fault-joins-2-1.md)), and so does
  `Rule`, the qualified name of the enum or closed-set member that refused, which the problem carries
  as `rule` ([decision 67](adr/0067-rule-that-refused-on-fault.md)); equality compares the rule's type,
  since two types' rules can read alike ([decision 72](adr/0072-rule-identity-is-its-type.md)).
- **`Fault<T1, T2>` and `Fault<T1, T2, T3>` (2.1.0)** hold one fault of one of several types, so a
  boundary answers with its own faults beside those shared code decides, without copying them
  ([decision 78](adr/0078-fault-sets-compose-side-by-side.md)). A fault of each type converts; a bare reason
  does not, and `Equals` with one is a compile error, as for `Fault<TFault>`. Where a fault becomes a
  `Result` of one, C# would need two user-defined conversions, so `Of(reason)` and `Of(fault)` name the
  type. A `default` one holds no fault, `Match` throws for it, and CMTK0001 refuses writing it; a
  `default` `Fault<TFault>` of a class reason holds no reason and does not convert, while one over an
  enum holds the member valued 0, which nothing tells from a real one.
- **Pipelines (decided 2026-09-28).** `MapAsync`/`BindAsync` take an asynchronous selector on a
  result, and every combinator of both shapes continues a `Task<Result<…>>` (`MapAsync`, `BindAsync`,
  `MapErrorAsync`, `TapAsync`), so a chain is awaited once, at its end; a completeness test holds
  every combinator to a continuation. `Match` ends a chain after that `await`. Each continuation
  checks its delegate before awaiting, so a null one is reported even when the pending result fails.
  `Zip` combines two to six results, `Sequence` and `Traverse` a sequence of them. All stop at the
  first error, in argument or sequence order: collecting errors is the validation applicative the
  non-goals exclude. A zip still inspects every argument, so an uninitialized one after an error
  throws. Callbacks return `Task` ([decision 30](adr/0030-asynchronous-callbacks-return-task.md)): `TapAsync` and `TapErrorAsync` take a
  `Func<…, Task>` beside the `Action`, so an `async` lambda is awaited; a `ValueTask` one needs
  `.AsTask()`.
- **A callback runs where the task it waited for completed (decided 2026-10-13,
  [decision 62](adr/0062-asynchronous-callbacks-run-where-awaited-task-completed.md)).** Every `await`
  in the package is `ConfigureAwait(false)`, so nothing resumes on the caller's
  `SynchronizationContext` and its awaits cannot deadlock a caller blocked on a chain. The price, measured: a
  callback after a pending task runs without the caller's context, one after a completed task runs at
  once on it, so in a UI or a Blazor component a callback that touches dispatcher-bound state works or
  fails by timing. That is documented rather than changed: await the task and call the synchronous
  combinator, or marshal inside the callback. `AsynchronousContextTests` holds every public member
  that returns a task to it.

**Decided 2026-09-27: an uninitialized `Result` throws.**
- A `default` result is `State == Uninitialized`. If the branching members tested only
  `State == Success`, it would take the **error** branch with `default(TError)`.
- For an enum fault that is the first member (e.g. `Blank`): a plausible, wrong reason. For a
  reference-type error it is `null`, despite `[NotNullWhen(false)]`.
- The analyzer blocks `default` in source, but array elements, class fields (CMTK0005/0006) and
  reflection still produce such instances.
- **Decision:** every member that picks a branch (`Match`, `Map`, `Bind`, `Tap`, `TapAsync`,
  `TapErrorAsync`, `TryGetValue`/`TryGetError`, `AsEnumerable`, the `bool` conversion of
  `Result<TError>`) throws
  `InvalidOperationException` on `Uninitialized`. That is a loud failure instead of a fabricated
  fault. `State`, equality and `ToString` stay safe to call.

**Decided 2026-09-27: `Option`/`Result` are not wire types.**
- Measured: with private state and no converter, System.Text.Json wrote `{}` for a `Some` and read
  it back as `None`, and wrote `{"State":1}` for a success and read it back as `Uninitialized`.
  Nothing raised.
- **Decision:** `NotWireTypeJsonConverterFactory`, on `Option<T>`, both `Result` shapes and the
  `Success`/`Success<T>`/`Error<T>` markers, throws `NotSupportedException` from `Read` and `Write`,
  naming the type and the alternative and never the content. `HandleNull` is on, so a JSON `null`
  gets the same message, and a dictionary key does too. That is what the serializer itself does for
  `System.Type`. It hands every type one converter typed `object`, which the serializer wraps: a
  converter per type needed `MakeGenericType`, and under Native AOT that failed for these structs
  with the runtime's message instead of this one ([Native AOT](native-aot.md)).
- A serialized shape says absent with `T?`; `ToOption()`/`OrNull()` convert at the boundary; a
  result is matched to a response or a document. An application that wants a wire format registers
  its own converter on its options, which takes precedence over the type's attribute, in a
  source-generated context too (measured).
- **Not a lossless converter** (`Some(x)` as `x`, `None` as `null`): its bytes are identical to
  `T?`, so it would only move the CLR type onto the shape the design keeps it off, and a `Result`
  has no sensible wire shape at all.
- **Not an analyzer:** whether a type is serialized is a property of the application's roots and
  options, which the package cannot see. An application can add a shape test of its own.
- **Known gap:** the serializer calls no converter for a property that is absent from the document,
  so it stays `default`. `required` or `RespectRequiredConstructorParameters` closes it, and the
  README says so.
- Completeness: `NotWireTypeTests` walks every exported struct of the assembly, so a struct added
  later refuses too, or is exempted on purpose.

**Why not an existing package** (checked 2026-09-26). The criteria: no public `.Value`/`.Error`,
a generic `TError`, `default` is not success, maintained.
- **Funcky 3.6.0** is closest (`[NonDefaultable]` enforced by an analyzer, no `.Value`), but its
  `Result<T>` fixes the error type to `Exception`.
- **nlkl/Optional** has the right Option shape but no Result, and no release since 2018.
- **CSharpFunctionalExtensions, DotNext and FluentResults** expose a throwing `.Value`.
- **ErrorOr, Remora.Results and Ardalis.Result** expose an unguarded one. With ErrorOr,
  CSharpFunctionalExtensions and Remora, `default(Result<…>)` reads as success.
- **JSON** (checked 2026-09-27): CSharpFunctionalExtensions 3.7.0 ships opt-in converters that write
  a `Result` as a DTO with `IsSuccess`/`Error`/`Value`, for passing outcomes between services;
  LanguageExt (v5 still beta) ships none; ErrorOr, OneOf and FluentResults ship none and serialize
  their public properties as they are. None refuses.

C# 15 `union` types (in .NET 11 preview) ship no standard `Option`/`Result`, and they need .NET 11
anyway, while this repo targets net10.0.

**Against the value-object libraries** (checked 2026-09-27):
- Vogen validation returns Ok/Invalid with a plain string, and its `TryFrom` is `bool` + `out`.
- Thinktecture returns a typed `ValidationError` but ships no Result type.
- TypeKit's value objects return `Result<TVO, TFault>` from `Create` and `Option<TVO>` from
  `TryFrom`. That is the reason Option/Result and the value objects ship together.
