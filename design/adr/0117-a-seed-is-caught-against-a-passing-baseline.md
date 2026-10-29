# 0117. A seed is caught against a passing baseline, by a test of its own

Date: 2026-10-29
Status: accepted

Context: [#151](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/151), found by the 2.3.0 pre-release
review. [Decision 112](0112-the-prober-gives-way-to-valueranges-oracle-kit.md) made `oracles seeds` the release's
proof that every oracle can fail; [decision 108](0108-a-probe-check-earns-credit-against-a-baseline-and-ws2-closes.md)
had held the prober it replaced to a baseline, and the port did not carry that over.

`oracles seeds` credited a seed whenever one test of its oracle failed with a row that was not a known candidate,
whatever the failure was. On an arm64 Mac, SQL Server 2022 under QEMU stops before it is ready, so the SQL Server
collection fixture throws `ContainerNotRunningException` in `InitializeAsync` and every test of both SQL Server
oracles fails, with and without a seed. The tool reported both seeds `CAUGHT` (10,071 of 10,071 and 170 of 170 tests
failing) and exited 0, ending `every seed is caught`, while no query or insert under review had run.

**A seed is caught only when its oracle passes without it and fails with it, in a test of its own.** `oracles seeds`
first runs every oracle the selected seeds name on a copy of the tree with no seed, built and filtered as the seeded
runs are, in the same process and environment. An oracle that does not pass there (a run that never reached its
tests, or a test that fails on a row that is not a known candidate) makes each of its pairs `ERROR`, and is not run
with the seed. A baseline whose only failures are known candidates passes, since an unfiled candidate fails its sweep
until it is filed.

**A run that did not reach the tests' own code is `ERROR`, never `CAUGHT`, with or without the seed.** What the TRX
holds decides it, measured with xunit.v3 4.0.2 on Microsoft Testing Platform on 2026-10-29, one class per kind of
failure:

- A class, collection or assembly fixture that throws in its constructor or `InitializeAsync` fails every test that
  uses it, each with xunit's own message (`Collection fixture type 'T' threw in InitializeAsync`, the fixture's
  exception on the next line, the stack only an inner one). xunit.v3.core 4.0.2 holds four more setup messages of
  the same kind: unresolved constructor arguments, more than one public constructor, constructor parameters without
  fixture data, and a type it cannot create. A failed test whose message begins with one of them is setup.
- A fixture that throws in `Dispose` or `DisposeAsync` fails no test: the TRX counts it as an `error` in its
  `Counters` and holds no result for it. A nonzero `error`, `aborted`, `timeout` or `disconnected` counter is an error.
- A test host that crashes (`Environment.FailFast`) writes no TRX and exits 134. Microsoft Testing Platform exits 0
  when every test passes and 2 when one fails; any other exit code, or no TRX, is an error.
- A run whose every test is skipped ran nothing, and is an error.
- A failure in the test class's own constructor or `InitializeAsync` is written like one in the test's body: the
  exception's message, and a stack trace under the test's code. It counts as the test's: the oracles build there
  what they assert on (`KeyGenerationVariantTests` opens its connection in its constructor), and the baseline has
  shown that it passes without the seed.

The rule is one file, `test/oracles/SeedVerdict.cs`, compiled into the tool and into `SeedVerdictTests`, which hold
it on TRX documents of the measured shape. With the setup check and the baseline taken out, the dead fixture of #151
reads as caught again and three of those tests fail.

**`ERROR` exits 4**, beside 1 for a seed missed, so a release script tells a seed that needs a stronger oracle from an
environment that needs fixing. A release on an arm64 Mac runs `oracles seeds` with `TYPEKIT_MSSQL_IMAGE` naming
Azure SQL Edge, as the suite does (CONTRIBUTING's Docker bullet).

Rejected: telling a setup failure apart by its stack trace (a frame of `InitializeAsync`, a constructor), since a
test's own exception passes through product code whose frames carry the same names, and xunit writes the fixture
cases in fixed words of its own; and requiring the seeded run to fail on the rows the baseline passed, row by row,
since a test's rows are not in the TRX and the baseline already rules out a failure that does not depend on the seed.
