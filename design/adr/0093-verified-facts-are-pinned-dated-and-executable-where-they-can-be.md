# 0093. Verified facts are pinned, dated and executable where they can be

Date: 2026-10-15
Status: accepted

Context: 2026-10-15, the sessions that built [decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)'s
tooling and replayed the probes. Each researched again what an earlier session had settled: when ASP.NET's API
explorer lists an MVC controller, how the OpenAPI 2.0 writer treats a nullable body (the probe finding G1, which
was no TypeKit bug), when `TypeDescriptor` refuses a registered lookup (the fix 1ee02fc), and how Windows treats a
rename over an open file (two failed `windows-latest` runs). What the repository had learned about the frameworks
it builds on was spread over "measured" remarks in 47 design and docs files, spike READMEs, and
notes outside the repository that CI and other contributors cannot read. Nothing said which of them still held
after a dependency moved.

## Decision

**One store, one fact per file.** A fact about a framework, a platform or a tool TypeKit builds on is
`design/knowledge/KB-NNN-slug.md`, numbered in the order facts are added and never renumbered. Its index is
[design/knowledge/README.md](../knowledge/README.md). A fact states what the framework does, not what TypeKit
does about it; that is a decision record's, which cites the fact by its id.

**Frontmatter.** Each entry opens with YAML frontmatter that `docs-query check` reads:

- `verified:` the date the fact was last checked, `YYYY-MM-DD`;
- `pins:` what it was checked on, one item per dependency: `{PackageId} {version}` for a package of
  `Directory.Packages.props`, or `sdk {band}` (`sdk 10.0.4xx`) for what the SDK that `global.json` pins brings: the
  runtime, ASP.NET Core's shared framework, the compiler; or `external {name} {version}` for something the
  checkout never installs, a competitor's release, which no check compares;
- `source:` the primary source it was checked against: the framework's own output, measured (with what was run),
  its source code at a tag, or its documentation, named;
- `turned-on:` what in this repository depended on it: a decision, a fix, a probe finding;
- `test:` the characterization test that observes it, `test/CodoMetis.TypeKit.Facts/KBNNN.cs`, or `none, ` and
  why no test can.

Each value is a plain YAML string, so that GitHub renders the frontmatter: no `: ` or ` #` inside it, and no
quote, backtick or bracket first. The check refuses one that is not (`fact-frontmatter`).

**The admission bar.** A fact gets in only if

1. something here turned on it: a decision, a fix or a probe's verdict would have gone another way without it;
2. it is pinned and falsifiable: it names the versions it holds for, and a test, a document or a measurement
   could show it false;
3. it names the primary source it was checked against. A blog post, an answer on a forum, or a model's
   recollection is a lead to a source, never the source.

General knowledge of a framework does not get in. Neither does a fact about TypeKit itself, which its tests own.

**Executable where it can be.** A fact a test can observe has a characterization test in
`test/CodoMetis.TypeKit.Facts`, a class named by the fact's id (`KB007` for KB-007) that asserts what the framework
does on its own. The project references no TypeKit project, and `NoTypeKitTests` holds it to that, so a failure
there says that a framework changed, never that TypeKit did. It runs in CI with every other test, on Linux, Windows
and macOS. A dependency bump that changes the behaviour fails it, and that failure is the trigger to re-verify the
entry: correct the fact or its test, and look at what `turned-on:` names. A fact only prose can hold, a sentence of
a framework's documentation or a competitor's feature, keeps its pins and is checked by them.

**The authority rule.** A fact whose pin no longer matches what the checkout installs is stale, and a stale fact
may not settle a decision until it is re-verified on what is installed: its test passes there, or its source is
read again, and then its pins and `verified:` date move. `docs-query check` compares each pin with
`Directory.Packages.props` and `global.json` and reports an entry that is behind (`fact-behind`), ahead
(`fact-ahead`) or names nothing installed (`fact-not-installed`); it reports a fact without its test and a test
without its fact the same way. An `external` pin names nothing to compare with: such a fact is as fresh as its
`verified:` date, and its source is read again before a new decision rests on it. `RepositoryTests` requires the
check to be clean, so a bump of a pinned dependency fails CI until every fact it touches is re-verified. That is
deliberate: the bump is the moment the fact can have changed, and the list of entries is the work. The check is
C#, deterministic and needs no model ([decision 92](0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)).

**Where it is read.** Before researching a framework question, an agent asks `docs-query entry KB-NNN` or
`docs-query find` (AGENTS.md, "Finding code"). When research settles a question something turned on, the answer
becomes an entry. A probe's accepted difference cites a KB id or a record.

## Evidence

Seeded on 2026-10-15 with nine facts the repository had already paid for, each from the measurement or the record
that settled it, and each executable one re-measured by its test in `test/CodoMetis.TypeKit.Facts` before it was
written down (17 tests, beside two that hold the project free of TypeKit). One is prose: the tools' launch costs (KB-005), which a shared CI runner measures too
noisily to assert. The Windows fact (KB-004) asserts its Windows half on `windows-latest` and its Linux and macOS
half on the other two legs. The competitor facts behind `docs/comparison.md` are not in the store yet; they join as
prose facts, pinned to the versions the comparison names, when they are moved in.

Rejected: a prose-only store, which is what the scattered remarks already were; nothing would have said when one
went stale. Rejected: a fact store outside the repository, which CI cannot read and which no bump can fail.
