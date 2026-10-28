# docs-query

An index over the repository's Markdown that answers with pointers (file, heading path, line), never
with prose. Read the line it points at; that is what the answer is for.

```bash
dotnet build tools/DocsQuery -c Release                  # once; `dotnet build` of the solution builds Debug
dotnet tools/DocsQuery/bin/Release/net10.0/docs-query.dll entry CMTK0010
```

| Command | Answers | Contract |
|---|---|---|
| `entry <id>` | What an id names: a record (`61`, `0061`, `decision 61`, `ADR-0061`), a diagnostic (`CMTK0004`), a verified fact (`KB-007`, `KB007`), a path (`docs/json.md#preserve`), an anchor (`#preserve`), a spike, package or guide name, a heading's text. Definitions first, then index rows, labelled | exact |
| `outline <doc>` | A document's or section's headings, with lines and anchors | exact |
| `cites <id>`, `cited-by <id>` | Typed edges with their source line: `link`, `superseded-by`, `mention` (an id in prose, unlinked), `code` (an id in a fenced block). `--depth N` follows `link` and `superseded-by` only | exact |
| `list [filter...]` | Documents: `kind=adr status=open`, `date>=2026-10-01`, `title~json` | exact |
| `find <words>` | BM25 over the sections of the ranked documents; `--top N`, `--kind K`. A section that defines an id the query names comes first. A miss proves nothing | ranked |
| `claims [page]` | Every C# block, every line whose comment states an output, and every other fenced block of the README and the docs pages, with the sample region that compiles it; `--kind csharp\|stated\|block`, `--json` | exact |
| `check` | Broken links, missing anchors, ids defined nowhere, and the verified facts' rules (below) | exact |

`--json` prints one object per line, the summary first; a pointer is `path` and `line`. Exit codes: 0
found, 1 nothing found (or problems, for `check`), 2 usage, 3 stale dll refused.

## What it reads

The root README, `src/*/README.md`, `src/*/AnalyzerReleases.*.md`, `docs/*.md`, everything under
`design/`, and `spikes/*/README.md`; the kind comes from the path. `design/plan.md`,
`design/adr/README.md`, `design/knowledge/README.md` and the analyzer release tables are **indexes**: they resolve lookups (`entry`
shows their rows, labelled) and are left out of ranking, so a record and the row describing it never
compete. The samples project's `// sample:` regions are read so a claim names the region compiling it.

An id is defined by a record's title, a fact's file (`design/knowledge/KB-007-slug.md` defines `KB-007`), a
heading that starts with it (`## CMTK0004`), or a table row
or list item that opens with it (`| CMTK1011 | … |`, `- CMTK1011: …`). Search terms keep the
repository's identifiers whole: `CMTK0004`, `decision 61` (as `adr:0061`), `§3.1.4`, `2.1.0`,
`IValidatedValue<TSelf, T, TFault>` (and its bare name), `ReferenceHandler.Preserve` (and its parts).

A claim's `id` is `{page}#{anchor}/{kind}-{n}`: it survives edits outside its section, while its
line moves. Its `hash` changes with its content.

## Verified facts

A fact (`design/knowledge/KB-NNN-slug.md`, design/adr/0093) has YAML frontmatter, read as fields: `verified:`,
`pins:` (one field per list item), `source:`, `turned-on:`, `test:` and, where its measured answer is data, `fixture:`
(design/adr/0107). `list kind=fact` shows each with the date it was verified, and `check` holds every fact to these
rules:

| Problem | When |
|---|---|
| `fact-behind`, `fact-ahead` | A pin `{PackageId} {version}` differs from `Directory.Packages.props`, or `sdk {band}` (`sdk 10.0.4xx`) from the band of the SDK `global.json` pins. `external {name} {version}` is never compared |
| `fact-not-installed`, `fact-bad-pin` | A pin names a package neither file installs, or is not one of those forms |
| `fact-incomplete`, `fact-frontmatter` | A field is missing, `verified:` is no `YYYY-MM-DD`, or a value is not a plain YAML string (a `: ` or ` #` inside, a quote or backtick first) |
| `fact-untested` | `test:` is neither `none, ` and a reason nor a file in `test/CodoMetis.TypeKit.Facts/` that declares the class named by the id (`KB007`); a note in parentheses after the path, such as where the test runs, is not part of it |
| `fact-orphan-test` | A `KBNNN.cs` in the facts project that no fact names |
| `fact-fixture-missing`, `fact-fixture` | A `fixture:` whose file does not exist; one that is not `KBNNN.fixture.json` beside the test, or belongs to a fact without a test |
| `fact-orphan-fixture` | A `KBNNN.fixture.json` in the facts project that no fact names |

The pins are read from the checkout at each `check`, never from the index.

## Freshness

- **The index** is one gitignored file, `.docs-query.cache` at the root. Its fingerprint covers the
  sorted relative paths and SHA-256 of every input, the schema version and the tool's version; never
  a timestamp. When it differs the index is rebuilt into a temporary file and renamed over the old
  one, so a reader sees the old index or the new one, whole. Deleting the file costs one rebuild
  (about 0.25 s).
- **The dll** embeds the SHA-256 of each of its sources at build time, as every tool does
  (`tools/Directory.Build.targets`). At startup it hashes them again; on a difference it refuses
  with exit code 3 and names the command that rebuilds it, `dotnet build tools/DocsQuery -c Release`
  (design/adr/0092).

BCL only, no package references, never packed (`PackabilityTests`). Ordinal comparisons throughout.
