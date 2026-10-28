# Spike: Metalama.Testing.AspectTesting beside xunit.v3 on Microsoft Testing Platform

**Question.** Can `Metalama.Testing.AspectTesting` 2026.1.30 (MIT) test this repository's aspects? It
runs on xunit v2 (it depends on `xunit.extensibility.core`, `xunit.extensibility.execution` and
`xunit.assert` 2.9.3), and this repository runs xunit.v3 4.0.2 on Microsoft Testing Platform, chosen for
`dotnet test` by `"test": {"runner": "Microsoft.Testing.Platform"}` in the root `global.json`. In detail:

1. Can an aspect-test project live in a project of its own and run its tests, and with which runner?
2. Added to `CodoMetis.TypeKit.slnx`, would `dotnet test --solution` still run the rest?
3. If not, can it run as a separate command, locally and in CI?
4. Does it test the real `src/CodoMetis.TypeKit.Generators`, whose aspects are `internal` and applied by a
   `TransitiveProjectFabric`, through a project reference rather than a copy?
5. What do the expected files look like for a plain value object, a validated one and a closed set, and
   can a reviewer verify one by eye? (GitHub issue #93; the motivation is the reviewer.)

**Answer: it works, beside the solution, not inside its test run.** Measured 2026-10-28 on macOS arm64,
.NET SDK 10.0.403 (the root pin 10.0.402 with `latestPatch`), Metalama.Framework and
Metalama.Testing.AspectTesting 2026.1.30, xunit 2.9.3, xunit.runner.visualstudio 3.1.5,
Microsoft.NET.Test.Sdk 17.14.1 (VSTest 18.7.0).

1. Yes, as an xunit v2 project on VSTest, which needs a `global.json` of its own: the root one makes
   `dotnet test` refuse it. With this folder's `global.json` (the same SDK pin, no `test` key),
   `dotnet test` run from this folder passes 4 of 4.
2. No. One VSTest project in the solution makes `dotnet test --solution` refuse the whole run, before
   building, and no test of the other projects runs. Marked `IsTestProject=false`, the project is skipped
   by the solution's test run, which then runs the rest, but VSTest's `dotnet test` then also runs
   nothing and exits 0.
3. Yes, as a separate command: `dotnet test` from this folder, or `dotnet vstest` on the built dll from
   anywhere. CI would need one more step with `working-directory` set to this folder. That is
   plausible but was not run in CI.
4. Yes. The project references the real generators project, and the fabric applies inside the test
   framework's compilation of each input file: the closed set, the plain value object and the validated
   one come out fully woven, and a refusal comes out as its CMTK diagnostic. A defect seeded in a scratch
   copy of `src/` fails the two tests whose output it changes.
5. The expected files have 235 to 349 lines each (15 to 23 KB) for an input of 5 to 17 lines. They are
   formatted, one member per block, so one member can be checked by eye. A whole file cannot, because
   it has every generated member in full, with every name fully qualified. Reading them turned up dead
   code after a `return` in every generated `TryParse(string?, …)` (finding 12).

## Layout

| Path | Role |
|---|---|
| `global.json` | The root SDK pin without the `test` key, so `dotnet test` run from here uses VSTest |
| `Directory.Build.props` | Stops the root props (warnings as errors, documentation, package validation) from reaching the spike |
| `AspectTests/AspectTests.csproj` | xunit 2.9.3, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio, Metalama.Testing.AspectTesting, and a project reference to `src/CodoMetis.TypeKit.Generators`. Versions are inline (`spikes/Directory.Packages.props` turns central management off) |
| `AspectTests/OrderId.cs` → `OrderId.t.cs`, `OrderId.OrderIdExtensions.i.cs` | A plain value object, `IValue<Guid>`, and the companion class it gets |
| `AspectTests/Email.cs` → `Email.t.cs`, `Email.EmailExtensions.i.cs` | The root README's validated `Email : IValidatedValue<Email, string, EmailFault>` |
| `AspectTests/OrderStatus.cs` → `OrderStatus.t.cs` | The root README's closed set |
| `AspectTests/NotReadonly.cs` → `NotReadonly.t.cs` | A refusal: a value object that is not `readonly` (CMTK1002) |

Every `.cs` file that is not a `.t.cs` or `.i.cs` is a test case, and its `.t.cs` holds the expected
output. A type that the aspects introduce in a file of its own gets a `.i.cs` file. Both kinds of file
are compared.

## Running it

```bash
cd spikes/AspectTestingSpike
dotnet test AspectTests/AspectTests.csproj        # VSTest, through this folder's global.json: 4 passed

# from anywhere, once built
dotnet vstest spikes/AspectTestingSpike/AspectTests/bin/Debug/net10.0/AspectTests.dll
```

A new or changed case writes its actual output to `AspectTests/obj/transformed/net10.0/`. On the first
run the framework creates a placeholder `.t.cs` (and `.i.cs`) that holds a `// TODO` line, so the test
fails. Accept the output by copying it over, which is what the package's `AcceptTestOutput` target does
(`dotnet build -t:AcceptTestOutput`). Commit first, then review the diff.

## Findings

| # | Claim | Result | Evidence |
|---|---|---|---|
| 1 | The package works on xunit v2 and VSTest, not xunit.v3 or MTP | measured | Its nuspec's dependencies for `net8.0` are `xunit.assert`, `xunit.extensibility.core` and `xunit.extensibility.execution` 2.9.3. Its `build/….targets` registers `Xunit.TestFrameworkAttribute("Metalama.Testing.AspectTesting.AspectTestFramework", …)`, an xunit v2 extension point. The 2026.1 documentation's sample project uses `Microsoft.NET.Test.Sdk`, `xunit` 2.x and `xunit.runner.visualstudio` ([snapshot-testing.md, release/2026.1](https://github.com/metalama/metalama.documentation/blob/release/2026.1/content/conceptual/aspects/testing/snapshot-testing.md)) |
| 2 | Under the root `global.json`, `dotnet test` refuses the project | measured | `dotnet test --project spikes/AspectTestingSpike/AspectTests/AspectTests.csproj` from the root: exit 1, `global.json defines test runner to be Microsoft.Testing.Platform. All projects must use that test runner. The following test projects are using VSTest test runner: AspectTests.csproj`. The same through `build-verdict test --project …`, which always runs from the root |
| 3 | With this folder's `global.json`, `dotnet test` from this folder runs it | measured | exit 0, `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 3 s`. The SDK reads the `global.json` nearest the current directory, so the same command from the root is finding 2 |
| 4 | `dotnet vstest` runs the built dll from the root, whatever `global.json` says | measured | exit 0, `Passed! - Failed: 0, Passed: 4` (with `IsTestProject` `false` and `true` alike) |
| 5 | One VSTest project in the solution stops `dotnet test --solution` for every project | measured on a scratch `.slnx` at the root (`tools/PrHygiene`, `test/CodoMetis.TypeKit.PrHygiene.Tests`, this project): exit 1, the message of finding 2, nothing built and no test run. The control, the same `.slnx` without this project: exit 0, 41 passed. The real `CodoMetis.TypeKit.slnx` was not changed |
| 6 | `IsTestProject=false` hides it from the solution's test run | measured on the same `.slnx`: exit 0, 41 passed. `AspectTests` is neither built nor named by `dotnet test`. `build-verdict build` of that `.slnx` builds it (7 projects) |
| 7 | …and VSTest's `dotnet test` on it then runs nothing and exits 0 | measured: from this folder, `dotnet test AspectTests/AspectTests.csproj` prints the restore and stops, exit 0, no test line. `-p:IsTestProject=true` brings back 4 passed. Setting it `false` only when `$(SolutionName)` is set does not help: the solution's test run still names the project as VSTest (exit 1, the message of finding 2) |
| 8 | The project's own compilation cannot hold the inputs | measured: the package sets `MetalamaEnabled=False` for the project, so the C# compiler sees the inputs unwoven, and the closed set fails with CS0535 (`All`, `Name`, `QualifiedName`, `TryParse` not implemented). The inputs are removed from `Compile`. The framework still finds them in the folder, as the documentation says it does with every file. The documented alternative, `#if METALAMA` around code that uses introduced members, does not help a declaration whose interface needs those members |
| 9 | The internal aspects and the `TransitiveProjectFabric` of the real generators apply in each test's compilation | measured: `OrderStatus.t.cs` has `_all`, `_name`, the static constructor naming each member, `All`, `Parse`, `TryParse`, the JSON and type converters. `OrderId.t.cs` and `Email.t.cs` have `_value`, the constructors, `Value`, `From`, `TryFrom`, `FromKnownGood`, parsing, formatting, comparison and converters, and each a `.i.cs` with its `GetValue` / `ValueOrNull` companion class. The project lists no compile-time assembly (`compile-time-assemblies.txt` is empty), so the framework reads the fabric from the referenced `CodoMetis.TypeKit.Generators.dll` in `assemblies.txt` (218 references) |
| 10 | An aspect diagnostic is part of the expected output | measured: `NotReadonly.t.cs` is the one line ``// Error CMTK1002 on `Draft`: `'Draft' is a value object and must be declared readonly. See …` ``, with no code. An expected error is a passing test |
| 11 | The tests catch a change in the generators | measured twice. Editing one name in the expected `Email.EmailExtensions.i.cs` fails `Email` (`Expected: …GetValu(this… Actual: …GetValue(this…`). In a scratch copy of `src/` and this spike, outside the repository, `ValueObjectParsableAspect.Provider` was seeded to ignore the caller's provider. That failed `OrderId` and `Email` and passed `OrderStatus` and `NotReadonly`, which do not parse through it (`Failed: 2, Passed: 2`, 7 changed lines in `OrderId.t.cs`). The repository's `src/` was not touched |
| 12 | Reading the snapshot shows what a test of behaviour does not | measured: every generated `TryParse(string?, IFormatProvider?, out …)` has an unreachable block after its `return`, a `__inner` declaration and a `try { __inner = …; } catch (FormatException) { return false; }`. It is in `OrderId.t.cs`, in `Email.t.cs`, and in the woven probes (`test/CodoMetis.TypeKit.Generators.Probes/obj/Debug/net10.0/metalama/Probes.cs`, `ProbeId`), so it is the real output, not the framework's. Its source is `ValueObjectParsableAspect.Templates.cs`, `TryParseTemplate`: the two `meta.InsertStatement` calls come after a compile-time `if (…) return …;` and are still emitted after the `return` |
| 13 | Size of the expected outputs | measured, with LF line endings: `OrderId.t.cs` 258 lines (19,721 bytes, longest line 1,078 characters, the base list of 14 interfaces), `Email.t.cs` 349 (23,264 bytes), `OrderStatus.t.cs` 235 (15,520 bytes), each `.i.cs` 18. 47 to 62 lines per file are longer than 120 characters. `[CompilerGenerated]` appears 25 to 52 times per file, `(global::System.Boolean)(bool)` 7 to 9 times |
| 14 | The output is reformatted, the input included | measured: the hand-written `Create` comes back with its `if` statements reformatted, and every member is on lines of its own. The woven file that the build writes (`MetalamaEmitCompilerTransformedFiles`, as the probes do) puts a member's attributes, signature and first statement on one line |
| 15 | Line endings do not matter to the comparison | measured on macOS: the framework wrote its output with CRLF, and git stores it with LF (`core.autocrlf`). With the expected files converted to LF, as a checkout gives them, the tests still pass (4 of 4). On Windows and Linux it was not run |
| 16 | Cost | measured: 4 tests in 2 to 3 s once built. The project builds the four `src/` projects it needs, in Debug |

## Consequences for the design

**A. It cannot join the solution's test run.** With MTP selected in the root `global.json`, a VSTest
project in the solution stops the whole test run (finding 5). The ways around that leave the project
built but untested, and a project that runs nothing and exits 0 is the failure this repository is most
wary of (finding 7). The aspect tests are a command of their own: `dotnet test` from this folder,
which has its own `global.json`, or `dotnet vstest` on the built dll. Only an MTP host for xunit v2
would remove the split. One exists on nuget.org (`YTest.MTP.XUnit2` 1.0.3). It is third-party, was not
tried, and nothing here shows it can host Metalama's custom test framework.

**B. CI would need one step and a check that it ran.** The step is `dotnet test
AspectTests/AspectTests.csproj` with `working-directory: spikes/AspectTestingSpike` (or wherever the
project ends up), after `setup-dotnet` with the root `global.json`, whose SDK is the same. Not run in
CI. Line endings do not matter to the comparison (finding 15), but whether the output is otherwise the
same on Windows and Linux was not measured. The CI matrix has both.

**C. It answers "what does the generator emit", at a reviewer's scale per member.** It tests the real
generators through the real fabric (findings 9 to 11), so it adds to the woven probes, the build
outcomes and `GeneratedSurface.verified.txt` without a copy of anything. A change to a template shows
up as a diff of the lines it changed, which is what issue #93 asks for. A whole `.t.cs` is too long to
verify by eye (finding 13), but a diff is short: the seeded defect changed 7 lines. Finding 12 shows the
reading finds things.

**D. The output is too verbose for the READMEs as it is.** Issue #92's generated-code blocks would need a
trimmed, simplified excerpt. The fully qualified names, the cast noise and a 1,079-character base list
are not what a consumer should read. A snapshot could be the source the excerpt is checked against.

**E. Still open.**
- Whether `// <target>` (trim the output to the marked declarations, per the documentation) or the
  HTML writer makes a whole file reviewable.
- Expected output on Windows and Linux (B).
- The IDE: Rider needs its xunit adapter configured to discover these tests (per the documentation),
  and was not tried.
