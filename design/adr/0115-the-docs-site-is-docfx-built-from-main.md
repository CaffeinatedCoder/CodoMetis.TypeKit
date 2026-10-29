# 0115. The docs site is DocFX, built from main, from the sources as they are

Date: 2026-10-29
Status: accepted

Context: [#115](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/115); CodoMetis.ValueRanges chose DocFX
in its spike ([CaffeinatedCoder/CodoMetis.ValueRanges#417](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/417));
[decision 113](0113-ci-runs-the-lanes-a-change-can-affect.md) holds the CI lanes this adds a path to.

The guides in `docs/`, the root README and the package READMEs could only be read as Markdown on GitHub: no
navigation beyond the README's Documentation table, no search across them, and the XML docs of every public
member shown only by an IDE.

**The site is DocFX, publishing to GitHub Pages from `main` alone.** `.github/workflows/docs.yml` builds
`docs/*.md`, `README.md`, `src/*/README.md`, `CHANGELOG.md` and the API pages of the run-time packages
(`CodoMetis.TypeKit`, `.EntityFrameworkCore`, `.AspNetCore`, `.Serilog`, `.Testing`) and deploys them on a push to
`main`, which is the released state: topics merge into `release/<version>`, which merges into `main` when it is
tagged. A release branch is never published, so the site always describes the version `dotnet add package`
installs. The Analyzers' surface is their rules and the Generators' is what they generate into a consumer, so
their READMEs are their reference and they get no API pages. DocFX is pinned in `.config/dotnet-tools.json`
(2.81.0) and configured in `site/docfx.json`, inside the repository because DocFX refuses a content root outside
its base directory. A second line (`/2.x/`, `/3.x/`) waits until two are supported at once; CONTRIBUTING.md says
how one would be added.

**The pages are the files as they are.** Nothing copies or edits a page for the site, so `ReadmeSampleTests` and
`OracleCatalogueTests` hold the text a reader sees. A link between two pages resolves as written, since every
page is a site page. A link to a file of the repository that is not a page (`design/`, `CONTRIBUTING.md`,
`AGENTS.md`, `SECURITY.md`, `LICENSE`) becomes a GitHub address at the built commit, rewritten by
`site/template/conceptual.extension.js` at build time. Those files are the build's resources, under `_repository/`,
so DocFX checks that each exists; the workflow then deletes them from the output. A link to a directory names no
file DocFX can check, so the README's one link to `spikes/` became an absolute address on `main`, which
`DocumentationLinkTests` checks.

**A pull request that touches a page checks its links.** On a pull request into `main` or a release branch whose
paths a page is built from (`*.md`, `docs/`, `design/`, `src/`, `site/`, the SDK and tool pins), the workflow runs
the whole build with `--warningsAsErrors` and publishes nothing. Measured on 2026-10-29: a seeded link to a missing
page (`InvalidFileLink`), to a missing heading of a page (`InvalidBookmark`) and to a missing design record
(`InvalidFileLink`, through the resources) each fail the build; without them it reports no warning. An anchor into
a file that is not a page is left to `DocumentationLinkTests`, which reads every anchor as GitHub derives it.

**`site/**` is prose** in `tools/CiLanes`: the docs workflow and the conventions tests read it, and nothing is
built from it, so a change to it alone runs the prose suite. `ProseReaderTests` counts a literal naming `site` as
prose, so no test outside `test/Prose.slnf` reads it unseen. The workflow itself is a workflow, which decision 113
already classes as anything else. `DocsSiteTests` holds the menu (`site/toc.yml`) to the README's Documentation
table, in its order, and to every shipping package's README, and the API pages to every run-time package.

**The spike: DocFX lists the members of C# 14 extension blocks.** `src/` holds 35 `extension(...)` blocks, which
carry most of the `Option` and `Result` extensions, `AsText` and the `IQueryable` helpers, and ValueRanges had none.
Measured with DocFX 2.81.0 on the published assemblies:

- Every instance member of a block is listed on its static class as the extension method the compiler lowers it
  to, with its XML docs: `Option.FirstOrNone<T>(this IEnumerable<T> source)`, `StringTextExtensions.AsText(this
  string? text)`: 37 extension methods on `Option` and 52 on `Result`.
- The type a block extends lists them under "Extension Methods": `Option<T>`'s page shows `Select`, `SelectMany`,
  `Where`, `Zip`, `ToResult` and `Result.Traverse`. A member whose constraints the open type does not meet is
  left out there, as for any extension method: `OrDefault()` (`where T : struct`) and `OrNull()`
  (`where T : class`) are on `Option`'s page only.
- The signature shown is the lowered one, `this in Option<T> a`, not the `extension(...)` block it is declared in.
- The one static member of a block, `GuidValueExtensions.New()`, is listed as `New<TSelf>()` with its constraints,
  on `GuidValueExtensions` only, not as `OrderId.New()` on the value objects it serves.

So the API pages ship. The workflow fails if `Option<T>`'s or `Result<T, TError>`'s page stops listing its
extension-block `Select`, so a DocFX update that loses them fails there, not on a reader.

**Needed once from the maintainer:** Settings > Pages > Source set to "GitHub Actions". No workflow can set it,
and until it is set, the first deployment from `main` fails at `configure-pages`.
