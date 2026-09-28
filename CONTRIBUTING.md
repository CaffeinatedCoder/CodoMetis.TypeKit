# Contributing

Issues and pull requests are welcome. For a security report, follow [SECURITY.md](SECURITY.md)
instead. Please don't open a public issue for those.

## Getting set up

```bash
dotnet build CodoMetis.TypeKit.slnx
dotnet test --solution CodoMetis.TypeKit.slnx
./test/consumer-smoke-test.sh      # packs the packages and builds throwaway consumers against them
```

The EF Core tests start a PostgreSQL container, so the suite needs Docker. The smoke test publishes
one consumer with Native AOT, which needs the platform's native toolchain: clang (Xcode's command-line
tools on macOS; `clang` and `zlib1g-dev` on Debian and Ubuntu). Shipping projects live
under `src/` and test projects under `test/`.
[AGENTS.md](AGENTS.md) is the architecture guide and [docs/plan.md](docs/plan.md) the current
roadmap. Read both before changing `Option`/`Result`, an aspect, the fabric, the analyzer or a
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

- One concern per PR, with the reasoning in the description rather than only in the diff.
- Full suite green.
- User-visible changes get a changelog entry.
- Match the surrounding style. Comments explain *why*, especially where behaviour is load-bearing
  and non-obvious.

## Releasing

The five packages share one version and are released together, only through
`.github/workflows/release.yml` (see [SECURITY.md](SECURITY.md)).

1. In `CHANGELOG.md`, replace `Unreleased` in the version's heading with today's date
   (`## 1.0.0 — 2026-10-01`). The section becomes the GitHub release's notes.
2. Move the rules in `src/CodoMetis.TypeKit.Analyzers/AnalyzerReleases.Unshipped.md` to
   `AnalyzerReleases.Shipped.md`, under `## Release <version>`. Their ids and severities are public
   contract from then on. `ChangelogTests` fails until steps 1 and 2 agree.
3. Commit, then tag and push: `git tag v1.0.0 && git push origin v1.0.0`. The workflow checks the tag
   against `Version` and the dated heading, runs the suite and the smoke test, and waits for approval
   in the `nuget` environment before pushing.
4. Afterwards: raise `Version` in `Directory.Build.props`, set `PackageValidationBaselineVersion` to
   the version just released, so ApiCompat guards the public surface from then on, and open a new
   `## <version> — Unreleased` section. `ChangelogTests` fails while the baseline trails the last
   version the changelog dates.

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
