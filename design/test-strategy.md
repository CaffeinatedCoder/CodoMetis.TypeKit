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
