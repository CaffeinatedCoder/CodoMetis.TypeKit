# 0045. Topic pages in `docs/`, compiled like the READMEs; the plan in `design/`

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase
13.

The package READMEs stay the reference for each package, since nuget.org shows them and every
exception message and analyzer help link anchors into them. Guides, one topic each and written
around examples, are `docs/<topic>.md`, listed in the root README's "Documentation" table, which
`ReadmeSampleTests` reads: every page in `docs/` is compiled by being there (its samples are
`{file name}/{name}` in `test/CodoMetis.TypeKit.Samples/Docs`), must be linked from the table,
and the table links only pages that exist. The same code block is never in a README and a page,
since a block is compiled from exactly one region. A page's public-surface table (`option.md`,
`result.md`) is checked against the types by reflection, so a member added without a row fails.
The plan and the design notes moved to `design/`, so that `docs/` holds only what a consumer
reads and nothing there is exempt from compilation; `design/` is a solution folder. Eleven pages
came first; `merge-patches.md` and `closed-sets.md` joined them once the docs branch merged into
2.0.0 (2026-10-13). The fault responses are in the ASP.NET Core and closed-set pages, no public
aspect is planned ([decision 43](0043-exceptions-type-argument-constraints-constructor-guarantee.md) allows one, and none exists), and `migrating-to-2.md` lists what
builds or runs differently for 1.5 code.
