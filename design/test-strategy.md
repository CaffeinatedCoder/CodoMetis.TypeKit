# Test strategy

1. **Behaviour.** Every public member of `Option`/`Result` and every generated member has a
   behaviour test. Generated members are tested against woven probe types, never against code
   written by hand in the test assembly.
2. **Generated surface.** A small tool reflects over the woven probe types. For each value object
   it renders the public members, implemented interfaces and attributes as sorted text, which is
   committed and asserted. An aspect change that adds or loses a member then shows up in review
   instead of shipping silently. Package validation against the last release (1.5.1, for 2.0.0)
   guards the hand-written surface, and an intended break is suppressed alone
   ([decision 48](adr/0048-intended-break-suppressed-alone-baseline-stays.md)).
3. **Generated SQL.** `ToQueryString()` snapshots for `.Value`, `GetValue`, `ValueOrNull`,
   `StartsWith`, equality, and `Contains` over a list of ids.
4. **OpenAPI.** A probe host's emitted document, asserted per shape against a control that uses
   the wrapped type directly, so the tests follow what ASP.NET publishes for that type ([OpenAPI](openapi.md)).
5. **Performance.** Allocation tests hold the generated members to what the wrapped type allocates,
   and `benchmarks/` measures time against the wrapped types. The benchmarks are not a gate: they are
   run before a release and when a template changes.
6. **README and docs samples** (2026-09-28; the `docs/` pages 2026-10-13, [decision 45](adr/0045-topic-pages-docs-compiled-like-readmes.md)). Every C#
   block of the READMEs and of the topic pages in `docs/` is a region in
   `test/CodoMetis.TypeKit.Samples`, which references the shipping projects as a consumer does, with
   the analyzer on. `ReadmeSampleTests` requires each block to equal exactly one region and each region
   to be shown, so a sample that stops compiling fails the build and an edit on one side fails the test.
   A docs page is discovered by being in `docs/`, and the root README's "Documentation" table has to
   link it.
   An output stated in a comment is asserted against the README's own comment, found by the expression
   the test evaluates. A pre-release audit had found samples that did not compile (a bare fault as a
   `Result<T, TError>`, `quantity.Value;` as a statement, an ellipsis as a lambda body), CMTK0006 on the
   EF entity sample, and SQL the query does not produce; the consumer smoke test had found the first
   once before ([Option and Result](option-result.md)). Seeded: a README or sample edited alone, the bare fault back in both, the committed
   READMEs, a changed SQL, JSON or string output, an opt-out on a compiled block or without a reason, an
   unlabelled fence, and either parser finding nothing each fail their test.
7. **Build outcomes** (2026-10-13). A declaration the generators refuse is an error, which no compiling
   project can observe, so `BuildOutcomeTests` builds throwaway consumers against the generators and the
   analyzer and reads their errors: every refusal by id and message, and no LAMA error, which would
   name nothing the user wrote. A compiler error in a consumer hides every LAMA0611 inside a
   declaration's own file (measured with a seeded generic `Parse`: reported alone, unseen beside a
   CS0535), so the refused closed sets, which do not implement their interface, are built apart from
   the value objects, whose build must have no compiler error, and a declaration whose error is C#'s own
   (CS0111) has a consumer of its own (`EqualityOperatorBuildTests`).
8. **Verified facts** (2026-10-15, [decision 93](adr/0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)).
   What a framework does on its own, where something here turned on it, is a
   [verified fact](knowledge/README.md), and each one a test can observe has a characterization test in
   `test/CodoMetis.TypeKit.Facts`, named by its id, in a project that references no TypeKit code
   (`NoTypeKitTests`). A bump that changes the behaviour fails there, not in a TypeKit test that would only
   look broken. `docs-query check`, which `RepositoryTests` requires clean, fails a bump that leaves a fact's
   pin behind. Seeded: a pin behind read as current, `Microsoft.AspNetCore.OpenApi` raised to 10.0.13 alone
   (KB-001 and KB-002 reported), a TypeKit project referenced from the facts, and KB-003's calls in the
   other order each fail their test.
9. **Variant catalogues** (2026-10-15, [decision 95](adr/0095-a-fix-is-tested-across-its-input-class.md)).
   A fix is tested across the class of input its defect lives in, not only on the input a review named.
   For a rule over type arguments the class is `TypeArgumentSites`
   (`test/CodoMetis.TypeKit.Analyzers.Tests/Variants/`): 62 shapes through which an argument reaches a
   parameter, written and inferred, method groups, null-conditional calls, extension blocks, generic
   attributes, implementations reached through another declaration, methods bound by pattern, crefs and
   `nameof`, and generated code, each run with an argument that breaks the rule's constraint and one that
   meets it. CMTK0010 runs it under three constraints (`TypeArgumentConstraintVariantTests`). For a
   generated type the class is the `shape.*` axes of `.claude/probe-axes.md`, for a framework option the
   settings axes. Measured: at the origin of CMTK0010's chain of fourteen fixes the catalogue fails on 20
   sites, each fixed by a later member of the chain; seeded, without the rule's method-group and property
   registrations, 15 cases fail.
   What the argument is, the second axis, is `TypeArgumentKinds` beside it: 79 kinds (classes, records and
   structs in each modifier, enums, delegates, arrays, `dynamic`, interfaces and the value-object marker,
   the abstract bases, types built from a parameter, a parameter under each constraint, bound and
   attribute) and 37 pairs. `TypeArgumentConstraintKindTests` runs every CMTK0010 attribute against each,
   864 cases, with an oracle per attribute written from `docs/type-constraints.md`'s tables. Measured
   2026-10-15 at each of chain A's commits: six of its seven fixes about what an argument is (`0d6ba1a`,
   `2f8a854`, `7792801`, `d27cf2d`, `75fb3aa`, `6368c1c`) turn 35 cases green that fail at their parent;
   `1a9d7f2` fixed the library's own declarations, which no analyzer test reads. It found one gap still
   open, `[Unsealed]` taking the abstract bases nothing derives from, which waits for 3.0.0 (decision 34).
   For the EF satellite, whose translation defects escaped most (7 of 10 in the fix history, all silent),
   the catalogue is `KeyShapes`, `QueryShapes` and `WrappedKinds` in
   `test/CodoMetis.TypeKit.EntityFrameworkCore.Tests/Variants/`, each case a differential against the same
   entity over the wrapped type. `KeyGenerationVariantTests` builds 13 key shapes (alone, the root of each
   mapping strategy, abstract or not, three dependents whose key is a foreign key, composite, configured)
   keyed by 13 wrapped types on SQLite, Npgsql and SQL Server, 507 cases, and has SQLite fill the keys in.
   `QueryTranslationVariantTests` runs 5,200 queries over 22 wrapped types (three with their own
   `CompareTo`) by accessor, site (column, nullable column, collection element, captured list, navigation,
   JSON, query filter, named or not), converter (TypeKit's, named, and three of the application's own) and
   operation, on SQLite against the wrapped type's rows and SQL and against memory, and translated for
   PostgreSQL. The README's departures from parity are its oracle: a `char` or `Guid` key and a TPC root
   without a sequence are not generated, `.Value` on a foreign converter's column and an own `CompareTo`
   in an ordering are refused. Measured 2026-10-15 at each fix of chains H and I, the catalogue replayed at
   the fix and at its parent: all nine turn cases green (`7732936` 272, `63471e7` 579, `717de54` 111,
   `719ee82` 12, `8f6af8b` 12, `92dfe16` 12, `e644a8a` 67, `3bddba7` 13, `84e9776` 17), and three of them
   turn cases red that the chain's next fix repaired: `92dfe16` the abstract TPC root on Npgsql and SQL
   Server (6), `e644a8a` and `3bddba7` the `char` key (16 and 1). Seeded: without the TPC convention 16 key
   cases fail, without the foreign-converter check 579 queries, with element columns re-typed 272. It
   found one divergence still open: a nullable record-class value object compared with `<` keeps its null
   rows in memory, where the generated operators sort null first, and SQL drops them.
