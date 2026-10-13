# Analyzer package and rule set

**Packaging (decided 2026-09-27).** `CodoMetis.TypeKit.Analyzers` (netstandard2.0; analyzers and
code fixes under `analyzers/dotnet/cs`). `CodoMetis.TypeKit` takes a package dependency on it.
- **Why separate:** rules and fixes can ship without a base version bump, and a consumer can take
  a newer analyzer by referencing it directly. The code-fix assembly (which needs Workspaces)
  stays out of a runtime library package.
- **Why still a dependency of the base package:** fabric spike consequence A. Whoever can see the
  interfaces must get the guard.
- **Measured 2026-09-27 (SDK 10.0.401),** packed and consumed from a local feed:
  - A project that references only `CodoMetis.TypeKit` gets CMTK0001 and CMTK0002, and so does a
    project that reaches it only through another project.
  - With the default `exclude="Build,Analyzers"` on the dependency, the analyzer **still** loads,
    because the SDK takes analyzers from every package in the restore graph. `PrivateAssets="none"`
    is kept so the dependency says what is intended instead of relying on that.
  - What does drop it: a project reference with `ReferenceOutputAssembly="false"` emits **no
    dependency at all**. The reference is therefore an ordinary one with `Private="false"`.
- **Guards:** `AnalyzerPackagingTests` packs both packages and reads the nuspec and the package
  layout, the code-fix assembly beside the analyzer's included. The consumer smoke test (phase 6)
  asserts CMTK0001 fires, directly and transitively.

Attributes stay in `CodoMetis.TypeKit`. The analyzer resolves them and the interfaces with
`GetTypeByMetadataName` and compares symbols ([Discovery](discovery.md)), so it does not reference the base package.
Inside the analyzer, rules that react to attributes are kept apart from value-object rules, so the
split stays mechanical if a generic analyzer package is ever wanted.

**Scope.** Code that compiles but is wrong about value objects, `Result`/`Option`, types that
forbid `default`, and what a type declares by attribute because C# cannot express it (a constraint
on a type parameter, a constructor a host needs). General linting (clock types, style) stays out, since Meziantou and Roslynator
exist.

| Id | Rule | Severity | Status |
|---|---|---|---|
| CMTK0001 | No `default`/`default(T)`/`new()`/`new T()` of a value object or a `[RequireCustomInitialization]` type. An operand of `==`/`!=`, or an argument of a call whose name has the word `Equal`, `Equals` or `Compare` or ends in `Be` (`ThrowIfEqual`, `Assert.NotEqual`, `ShouldNotBe`), is exempt: a guard check is the only defence against the defaults the rule cannot see. So is a `default` assigned to an `out` parameter of a method returning `bool`, the Try pattern. Where a sibling branch does not bind (same-project value objects), the target type is taken from the enclosing conditional, switch arm or collection | Error | shipped in 1.0.0 |
| CMTK0002 | Type implements `IValue<>`/`IValidatedValue<,,>`, but the compilation does not reference `CodoMetis.TypeKit.Generators`, so it is never woven | Error | shipped in 1.0.0 |
| CMTK0003 | `Result`/`Option` a call returns, dropped by an expression statement, awaited or not, or a `Task` of one converted to a plain `Task` (`Task Cancel() => CancelAsync();`); `_ =` opts out. `Tap`, `TapAsync`, `TapError` and `TapErrorAsync` on a stored result or task are exempt, since they return their receiver, and so is a chain of them on one (`r.Tap(log).TapError(warn)`, `(await pending).Tap(log)`, `pending.WaitAsync(t).TapAsync(log)`), measured as an Error's false report before 2.0.0 was tagged. Also a collection of results the statement made (an array, `ICollection<T>` or `IReadOnlyCollection<T>`, such as `await Task.WhenAll(ids.Select(orders.CancelAsync))`), and a method group of a `Task<Result>` method converted to a delegate returning `Task`; tasks held in variables are silent, and so is a lazy `IEnumerable<T>`, where nothing has run, unless a call that was given no result returned it and the statement materialises it. Also `Wait` on a task the expression made, tasks of results the statement made converted to plain ones (`Task.WaitAll`, `WaitAny`), and a `foreach` or `await foreach` over results its header made whose body never reads the iteration variable; a variable named `_` opts out. CA1806 can only enforce this per method name, not per return type | Error | shipped in 1.0.0 at Warning; Error in 2.0.0 ([decision 50](adr/0050-warnings-cmtk0003-alone-becomes-error-2.md)), its 1.x gaps closed first |
| CMTK0004 | `IValueObjectMaterializer<,>.Materialize`, `ValueObjectConverter<,>.Materialize` or a hand-written value object's public `Materialize`, called or referenced (expression trees included). The satellite's own call is compiled in the satellite; EF's compiled model is generated code, whose diagnostics are not reported (measured: reported, it is six errors in the smoke test's AOT consumer). `StoredJsonConverterFactory` goes through `IStoredJsonConverterSource`, not `Materialize` | Error | shipped in 1.0.0 |
| CMTK0005 | `new T[n]`, `stackalloc T[n]`, `GC.AllocateUninitializedArray/AllocateArray<T>` or `Array.Resize` of a no-default struct: every new slot starts as `default`. A constant length of zero (for `Array.Resize` and the `GC` methods too) or initial elements are silent | Warning | shipped in 1.0.0 |
| CMTK0006 | Field or auto-property of a no-default struct type in a class that no initializer, `required` or constructor sets; a non-public parameterless constructor (EF, serializers) and a record's copy constructor are exempt. Measured 2026-09-28 on the repository's EF entities and request types: 12 of 12 reports on settable properties of a type with only the implicit constructor, each a real way to a default. `required`, the fix the message names where it compiles, works with EF's compiled model, precompiled queries and Native AOT (the smoke test rerun with it). A `[SetsRequiredMembers]` constructor must assign the `required` members it promises | Warning | shipped in 1.0.0 |
| CMTK0007 | `FromKnownGood` given a value straight from a parameter of the enclosing method or lambda, or a member or element of one. A value the code produced itself is legitimate and silent; a parameter copied into a local first is not seen. Info, since test theories and helpers that only receive constants are reported too | Info | shipped in 1.0.0 |
| CMTK0008 | The wrapped values of two different value objects compared (`==`, `!=`, ordering, `Equals`, `CompareTo`) through `.Value`, `?.Value`, `GetValue()` or `ValueOrNull()`: the typed ids unwrapped into the bug they prevent. Recognised by syntax, since `.Value` of a same-project value object does not bind where analyzers run. Also `string.Equals`, `object.Equals`, a comparer's `Equals`, `string.Compare`/`CompareOrdinal` over two such values (the forms MA0006 rewrites `==` into), and two value objects of different types through `Equals(object)`, which is always false. Also a LINQ join on two such values: a query's `join … on a.Value equals b.Value`, with or without `into`, and `Join`/`GroupJoin`/`LeftJoin`/`RightJoin` of `Enumerable`, `Queryable` and `AsyncEnumerable` whose key selectors are lambdas returning them, resolved by symbol, with the candidates where a key selector does not bind; a composite key is not looked into. A raw value on one side, or an explicit cast, is silent | Warning | shipped in 1.0.0 |
| CMTK0009 | A call that returns the `default` of a no-default struct when it finds nothing: `Enumerable`/`Queryable` `FirstOrDefault`, `LastOrDefault`, `SingleOrDefault`, `ElementAtOrDefault`, `DefaultIfEmpty` without a default value, `Nullable<T>.GetValueOrDefault()`, `GetValueOrDefault(dictionary, key)`, `Activator.CreateInstance`, `RuntimeHelpers.GetUninitializedObject`, and `Option.OrDefault()`. The message names `FirstOrNone`, `GetValueOrNone` or `Or`. Warning, since a sequence checked for emptiness first is correct. Also `ImmutableArray`'s own, `Find`/`FindLast` of `List<T>`, `Array` and `ImmutableList<T>`, `ImmutableDictionary.GetValueOrDefault` and the immutable builders', and the async `FirstOrDefaultAsync`, `LastOrDefaultAsync`, `SingleOrDefaultAsync` and `ElementAtOrDefaultAsync` of `System.Linq.AsyncEnumerable` and EF Core, whose type is the method's type argument; EF Core's only where the project references it | Warning | shipped in 1.0.0 |
| CMTK0010 | A type argument that breaks a constraint attribute on its parameter (`[Abstract]`, `[Sealed]`, `[Record]`, `[Concrete]`), at any closed use: a base type, an implemented interface, a member type, a local, a `typeof`, a call with its type arguments written (at the argument) or inferred (at the call), a method group whose arguments the delegate infers (at the group). A type parameter forwarded without the same attribute is a breach too, unless its C# constraints leave it no breaking argument: `struct`/`unmanaged` or a bound proving `[Sealed]` proves `[Sealed]`; `new()` or whatever proves `[Sealed]` proves `[Concrete]`; a bound that is or proves a record proves `[Record]` (CS8865); nothing but the attribute proves `[Abstract]`. `[Concrete]` accepts an enum, a delegate and `dynamic`, as `new()` and `struct` do for an enum. Both settled 2026-10-13 before the tag, measured as false reports (an enum, a delegate, `dynamic`; six forwarding forms) with no miss among their neighbours. The open generic in its own body and an unbound `typeof` are silent. Reflection is out of reach | Error | built 2026-10-13 for 2.0.0 (phase 13), a major, so Error is within [decision 34](adr/0034-errors-fixed-within-major-version-warnings.md) |
| CMTK0011 | A concrete class implementing or deriving from a type marked `[RequireParameterlessConstructor]`, directly or through another type, without a parameterless constructor of the required accessibility; a class declaring no constructor satisfies `Public`. A struct is never reported (decided 2026-10-13, before the tag): its parameterless constructor, implicit or declared, is public and can be nothing else (CS8958), so it satisfies `Public` as it is, and under any other accessibility the rule reported an Error only a suppression could answer, and the fix wrote a constructor that did not compile. A wrong accessibility is reported at the constructor. The code fix, the package's first, in `CodoMetis.TypeKit.Analyzers.CodeFixes.dll` beside the analyzer, writes or changes the constructor; a type with a primary constructor in any of its parts gets none (CS8862). It writes its own trivia and asks no formatter, which laid out the whole type again and wrote the machine's line endings: doc comments, attributes, comments and the members' layout stay, added lines take the members' indentation and the brace's line ending, a one-line type gets the constructor on its line | Error | built 2026-10-13 for 2.0.0 (phase 13) |
| CMTK0012 | A closed set's generated `Match` given an argument by position: every parameter is a `Func<TResult>` in declaration order, so reordering two members swapped their branches and compiled. Bound, only the generated signature counts; unbound (the declaring project, before weaving), a call named `Match` on a closed set with one argument per member, its names the generators' rule (the member's name, first letter lower-cased, a keyword escaped). The code fix names every argument after the parameter it binds to now | Error | built 2026-10-13 for 2.0.0 (phase 13), after a review found the swap |
| — | Code fixes for the aspect's shape diagnostics CMTK1000–1002 (missing `partial`/`record`/`readonly`) | — | proposed. The aspect keeps its own error as a backstop |

**Rules that read calls see the source before weaving (measured 2026-09-28).** Metalama runs
analyzers on the unwoven source, where a call to a member the generators introduce into a value
object of the same project does not bind. CMTK0003 recognises `X.TryFrom(…)` and `x.Revalidate()`,
CMTK0007 `X.FromKnownGood(…)` (a `using static` call included) and CMTK0009 `X.TryFrom(…).OrDefault()`
by the generated member's name on a validated value object's type, which binds; the names are pinned
by the generated-surface snapshot. CMTK0001 takes a `default`'s type from the enclosing conditional or
collection when a sibling does not bind, and CMTK0006 resolves an unbound `this(…)` to the one
constructor it can mean. A local declared `var` from an unbound call, and CMTK0008 over such locals,
stay out of reach. The build-outcome consumer holds every rule to a value object of its own project,
since the verifier tests never weave.

**Generated code (decided 2026-09-28).** The rules analyse generated code and report from it only
where a location maps (`#line`) to a `.razor` or `.cshtml` file: skipping generated code, as Roslyn
does by default, silenced every rule inside Razor components, CMTK0001 included. Anything else
generated stays unreported, which keeps EF's compiled model out of CMTK0004 (the smoke test's AOT
consumer is the measurement). `GeneratedCode.Report` is the one reporting path. CMTK0010 called
`ReportDiagnostic` itself until 2.0.0's pre-tag review, and so reported from every generated file;
`GeneratedCodeTests` holds each rule to the contract in a `.razor` and a `.cshtml` mapping, and
reads the analyzers' sources for a direct `ReportDiagnostic`, so a rule added later is held too.

**Transformed code (decided 2026-09-28, [decision 33](adr/0033-analyzers-run-code-metalama-transformed.md)).** Where Metalama compiles a project, source
generators run after its transformation, so the Razor compiler's output is not in the source an
analyzer sees, and neither are the members the aspects introduce. The analyzer package's
`buildTransitive` props lists the analyzers' namespace as a `MetalamaTransformedCodeAnalyzer`, and
Metalama's compiler runs them on the transformed code. The IDE ignores the item, so live analysis
still runs on the source, where the name-based forms above remain the reach.

**Release discipline.** Keep `AnalyzerReleases.Shipped/Unshipped.md` tracking (RS2008). A new rule
ships at Warning or Info in a minor version and is raised to Error only in a major. Consumers
build with warnings as errors, so a new Error rule in a minor version would break their builds on
update. Within a major version an Error never reports more than it did; a Warning or Info may learn
more forms in a minor version ([decision 34](adr/0034-errors-fixed-within-major-version-warnings.md), stated in the analyzer README).
