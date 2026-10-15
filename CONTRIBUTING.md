# Contributing

Issues and pull requests are welcome. For a security report, follow [SECURITY.md](SECURITY.md)
instead. Please don't open a public issue for those.

## Getting set up

A machine needs git and:

- **The .NET SDK that `global.json` names**, or a later patch of its feature band. Other bands and
  previews are not used, so `dotnet --version` in the repository root must print a version of that
  band. The pin moves with .NET's servicing releases; when it does, install the new patch. Install
  Microsoft's build, from [dot.net](https://dotnet.microsoft.com/download) or, on macOS, the
  `dotnet-sdk` Homebrew cask. Homebrew's `dotnet` formula trails the releases, and its Native AOT
  publish failed to link (`ld: library 'ssl' not found`); `brew shellenv` puts it first on `PATH`, so
  put `/usr/local/share/dotnet` before it, or `brew unlink dotnet`.

That is all the build and the tests need, on Windows, macOS or Linux: the repository's tools are C#,
a file-based app (`dotnet run tools/x.cs`) or a tool project under `tools/`, never shell or Python scripts
([decision 92](design/adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)). Two tasks need one
thing more each:

- **Docker, running** (Docker Desktop, OrbStack, Colima), for the PostgreSQL round-trip tests, which
  start a Linux container through Testcontainers. Without it those fail with
  `DockerUnavailableException`, and every other test passes.
- **The platform's native toolchain** for the smoke test, which publishes two consumers with Native
  AOT, a web host and a console application with EF Core: Xcode's command-line tools on macOS
  (`xcode-select --install`), `clang` and `zlib1g-dev` on Debian and Ubuntu, MSVC's C++ build tools
  (Visual Studio's "Desktop development with C++") on Windows.

An IDE started from the Dock or a launcher does not read the shell's profile: point its .NET CLI path
at the same SDK (Rider: Settings, Build, Execution, Deployment, Toolset and Build).

After an SDK upgrade, a build that fails with MSB4242 ("workload set … missing manifests") still has
the old SDK's workload set. Run `sudo dotnet workload update` in the repository root, where
`global.json` selects the SDK it updates; elsewhere it updates whichever SDK is newest there.

```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
dotnet build tools/ConsumerSmokeTest -c Release
dotnet tools/ConsumerSmokeTest/bin/Release/net10.0/consumer-smoke-test.dll   # packs the packages and builds throwaway consumers against them
```

Dependabot raises the GitHub Actions weekly and the test projects' packages monthly, except
Metalama, EF Core, ASP.NET Core and Roslyn. Those and the SDK pin are raised by hand: the shipping
references among them are floors every consumer restores against (`Directory.Packages.props`), so
raising one is a release decision.

Shipping projects live under `src/` and test projects under `test/`; the topic pages a consumer reads are in `docs/`, each
compiled like the READMEs, and the plan is in `design/`.
[AGENTS.md](AGENTS.md) is the architecture guide and [design/plan.md](design/plan.md) the map of the
design notes, the decision records and the roadmap. Read both before changing `Option`/`Result`, an aspect, the fabric, the analyzer or a
satellite.

## The quality bar

This package's characteristic failure is not a crash. It is code that compiles and does less than
it appears to. A value object that builds but was never generated. A `Result` whose failure was
silently ignored. An analyzer that stops reporting while every build stays green. A column that materialises a value the type would have refused. A
schema that publishes `{}` for a type the API accepts as a uuid. Nothing throws, and the consumer
finds out in production.

Everything below exists because of that failure mode.

**A bug fix needs a regression test, and the test must be proven to work.** Write the test, watch
it fail, apply the fix, watch it pass. Then *revert the fix and confirm the test fails again*. A
test that passes both with and without the fix is not a regression test. See
[`.claude/skills/verify-the-guard`](.claude/skills/verify-the-guard/SKILL.md).

**Every analyzer rule has a positive control per syntax form it registers.** An analyzer that
silently stops firing is the worst outcome this repository can ship, because nothing turns red.
The rule id and severity are asserted as literals: both are public contract.

**Generated code is tested through behaviour, not presence.** A build proves a member exists. It
cannot prove that `TryFrom` agrees with `Create`, or that JSON round-trips. Tests compare the
generated answer against the hand-written one on the same input.

**Discovery is by interface, never by name.** See AGENTS.md. A pull request that adds a namespace
string, an assembly-name prefix or a type-name list will be asked to replace it.

**Prefer a loud failure to a silent one.** Where the package cannot do something correctly, it
should fail the build or throw with an actionable message.

## Pull requests

Work for the next version goes into its release branch, `release/<version>` (`release/2.2.0`), not
into `main` ([decision 94](design/adr/0094-topic-branches-into-a-release-branch-and-fix-trailers.md)).
Branch from it as `<type>/<slug>`, where the type is your main commit's (`feat/…`, `fix/…`, `docs/…`,
`build/…`), and open the PR against it. PRs are merged with a merge commit, never squashed or rebased:
the design records cite commits by hash.

- One concern per PR, with the reasoning in the description rather than only in the diff.
- Full suite green.
- User-visible changes get a changelog entry.
- Match the surrounding style. Comments explain *why*, especially where behaviour is load-bearing
  and non-obvious.
- A `fix:` commit ends with two trailers, from the tables in decision 94: the defect's class and who
  found it. `CommitTrailerTests` fails without them.

  ```
  fix: a wrapped Parse returning a convertible type is generated again

  Defect-Class: GTS
  Found-By: CA
  ```

## Releasing

The packages share one version and are released together, only through
`.github/workflows/release.yml` (see [SECURITY.md](SECURITY.md)).

1. In `CHANGELOG.md`, replace `Unreleased` in the version's heading with today's date
   (`## 1.0.0 — 2026-10-01`). The section becomes the GitHub release's notes.
2. Move the rules in `src/CodoMetis.TypeKit.Analyzers/AnalyzerReleases.Unshipped.md` to
   `AnalyzerReleases.Shipped.md`, under `## Release <version>`. Their ids and severities are public
   contract from then on. `ChangelogTests` fails until steps 1 and 2 agree.
3. Commit on the release branch, open its PR into `main`, and merge it with a merge commit. Tag that
   merge commit and push the tag: `git tag v1.0.0 && git push origin v1.0.0`. The workflow checks the tag
   against `Version` and the dated heading, runs the suite and the smoke test, and waits for approval
   in the `nuget` environment before pushing. Delete the release branch once the tag is pushed.
4. Afterwards: raise `Version` in `Directory.Build.props`, set `PackageValidationBaselineVersion` to
   the version just released, so ApiCompat guards the public surface from then on, and open a new
   `## <version> — Unreleased` section. `ChangelogTests` fails while the baseline trails the last
   version the changelog dates. Once that is on `main`, cut the next `release/<version>` from it and
   push it.

`workflow_dispatch` with `dry_run` (the default) rehearses everything but the push.

## AI-assisted development

A substantial portion of this codebase was written with AI assistance, using
[Claude Code](https://claude.com/claude-code). This is stated plainly because adopters deserve to
know how the project is built, not because anything about it is unusual.

What that does and does not mean:

- **Direction, architecture and acceptance are the maintainer's.** Design decisions, trade-offs and
  what merges are human calls. AI is a tool used under review, not an autonomous committer.
- **Nothing is accepted because it looks right.** The verification discipline above exists
  because plausible-looking code is cheap to produce.
- **The audit trail is public.** Commit messages record what was wrong, what a consumer would have
  experienced, and how the fix was verified.
- **Code is judged on behaviour, not provenance.**

If you contribute with AI assistance, that is fine, and the same bar applies. Please make sure you
understand and can defend what you are submitting, and that it is yours to contribute.
