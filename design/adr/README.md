# Decisions

One record per decision, numbered as the plan numbered them, in the order they were taken, each with
its date and the evidence that settled it. A decision is changed by a new one that names it, never by
editing the old; a record that a later one replaces says `superseded by` and the number. The notes
per area ([design](../plan.md)) link to the records they rest on.

| # | Decision | Date | Status |
|---|---|---|---|
| [0001](0001-license-mit.md) | License: MIT | 2026-09-26 | accepted |
| [0002](0002-name-codometis-typekit.md) | Name: `CodoMetis.TypeKit` | 2026-09-27 | accepted |
| [0003](0003-analyzer-ids-cmtk.md) | Analyzer ids: `CMTK` | 2026-09-27 | accepted |
| [0004](0004-metalama-2026-1-x-latest-stable.md) | Metalama 2026.1.x (latest stable) | 2026-09-26 | accepted |
| [0005](0005-option-result.md) | Option/Result | 2026-09-27 | accepted |
| [0006](0006-result-shape.md) | `Result` shape | 2026-09-27 | accepted |
| [0007](0007-analyzer-own-package.md) | Analyzer as its own package | 2026-09-27 | accepted |
| [0008](0008-uninitialized-result-throws.md) | An uninitialized `Result` throws | 2026-09-27 | accepted |
| [0009](0009-ef-mapping-additive-irelationaltypemappingsourceplugin.md) | EF mapping through an additive `IRelationalTypeMappingSourcePlugin` | 2026-09-27 | accepted |
| [0010](0010-stored-json-storedjsonconverterfactory.md) | Stored JSON through `StoredJsonConverterFactory` | 2026-09-27 | accepted |
| [0011](0011-openapi-schema-transformer.md) | OpenAPI through one schema transformer | 2026-09-27 | accepted |
| [0012](0012-asp0020-documented-suppressed.md) | ASP0020 is documented, not suppressed | 2026-09-27 | accepted |
| [0013](0013-first-release-s-rule-set-cmtk0001.md) | The first release's rule set is CMTK0001–CMTK0009 | 2026-09-28 | accepted |
| [0014](0014-context7-json.md) | A `context7.json` links the repository to its Context7 entry | 2026-10-13 | accepted |
| [0015](0015-option-result-wire-types.md) | `Option`/`Result` are not wire types | 2026-09-27 | accepted |
| [0016](0016-native-aot-wherever-platform-allows.md) | Native AOT wherever the platform allows it | 2026-09-27 | accepted |
| [0017](0017-run-time-discovery-reads-generatedvalueobjectattribute-tvalueobject.md) | Run-time discovery reads `GeneratedValueObjectAttribute<TValueObject, T>` | 2026-09-27 | accepted |
| [0018](0018-name-per-concept.md) | One name per concept | 2026-09-27 | accepted |
| [0019](0019-concise-implicit.md) | Concise, not implicit | 2026-09-27 | accepted |
| [0020](0020-net10-0-only.md) | net10.0 only | 2026-09-28 | accepted |
| [0021](0021-first-release-1-0-0.md) | The first release is 1.0.0 | 2026-09-28 | accepted |
| [0022](0022-registration-extensions-live-framework-s-namespaces.md) | Registration extensions live in the framework's namespaces | 2026-09-28 | accepted |
| [0023](0023-value-object-key-over-integer-generated.md) | A value-object key over an integer is generated on add; one over a `Guid` is assigned | 2026-09-28 | accepted |
| [0024](0024-nested-value-objects-named-after-nesting.md) | Nested value objects are named after their nesting chain in the OpenAPI document | 2026-09-28 | accepted |
| [0025](0025-analyzer-s-roslyn-floor-stays-5.md) | The analyzer's Roslyn floor stays at 5.0.0 | 2026-09-28 | accepted |
| [0026](0026-value-object-s-json-wrapped-type.md) | A value object's JSON is its wrapped type's, byte for byte | 2026-09-28 | accepted |
| [0027](0027-no-refusal-quotes-input.md) | No refusal quotes the input | 2026-09-28 | accepted |
| [0028](0028-seams-declared-everything-else-refused.md) | Seams are declared, everything else refused | 2026-09-28 | accepted |
| [0029](0029-only-result-terror-converts-bool.md) | Only `Result<TError>` converts to `bool` | 2026-09-28 | accepted |
| [0030](0030-asynchronous-callbacks-return-task.md) | Asynchronous callbacks return `Task` | 2026-09-28 | accepted |
| [0031](0031-no-extension-object-unconstrained-t.md) | No extension on `object` or on an unconstrained `T` | 2026-09-28 | accepted |
| [0032](0032-value-object-holds-wrapped-value-alone.md) | A value object holds its wrapped value alone, and its equality is that value's | 2026-09-28 | accepted |
| [0033](0033-analyzers-run-code-metalama-transformed.md) | The analyzers run on the code Metalama transformed | 2026-09-28 | accepted |
| [0034](0034-errors-fixed-within-major-version-warnings.md) | Errors are fixed within a major version; Warnings may learn | 2026-09-28 | accepted |
| [0035](0035-sqlite-s-autoincrement-application-s-line.md) | SQLite's autoincrement is the application's line, not the convention's | 2026-09-28 | accepted |
| [0036](0036-nullable-value-object-admits-null-openapi.md) | A nullable value object admits null in the OpenAPI document wherever it appears | 2026-09-28 | accepted |
| [0037](0037-refusal-s-message-landing-page.md) | A refusal's message is its landing page | 2026-09-28 | accepted |
| [0038](0038-result-becomes-response-explicit-tohttpresult-faults.md) | A `Result` becomes a response through an explicit `ToHttpResult()`, its faults mapped in the host | 2026-10-11 | accepted |
| [0039](0039-value-object-s-fault-does-become.md) | A value object's fault does not become a validation error | 2026-10-11 | accepted |
| [0040](0040-codometis-typekit-testing-holds-assertions-hand.md) | `CodoMetis.TypeKit.Testing` holds assertions that hand back the content | 2026-10-11 | accepted |
| [0041](0041-sensitive-value-object-declares-tostring-serilog.md) | A sensitive value object is one that declares `ToString()`; Serilog gets a satellite | 2026-10-11 | accepted |
| [0042](0042-2-0-0-next-release-bundles.md) | 2.0.0 is the next release, and it bundles the 1.x candidates | 2026-10-12 | accepted |
| [0043](0043-exceptions-type-argument-constraints-constructor-guarantee.md) | Exceptions, type-argument constraints, a constructor guarantee and the text guards join 2.0.0 | 2026-10-13 | accepted |
| [0044](0044-2-0-0-net10-0-metalama.md) | 2.0.0 is `net10.0` on Metalama 2026.1; the version number names the API, never the runtime | 2026-10-13 | accepted |
| [0045](0045-topic-pages-docs-compiled-like-readmes.md) | Topic pages in `docs/`, compiled like the READMEs; the plan in `design/` | 2026-10-13 | accepted |
| [0046](0046-timeout-caught-result-catch-error.md) | A timeout caught by `Result.Catch` is the error | 2026-10-13 | accepted |
| [0047](0047-firstornone-query-runs-query.md) | `FirstOrNone` on a query runs in the query | 2026-10-13 | accepted |
| [0048](0048-intended-break-suppressed-alone-baseline-stays.md) | An intended break is suppressed alone; the baseline stays | 2026-10-13 | accepted |
| [0049](0049-fault-responses-client-can-act.md) | Fault responses a client can act on | 2026-10-13 | accepted |
| [0050](0050-warnings-cmtk0003-alone-becomes-error-2.md) | Of the Warnings, CMTK0003 alone becomes an Error in 2.0.0 | 2026-10-13 | accepted |
| [0051](0051-closed-set-parses-ignoring-case-only.md) | A closed set parses ignoring case only when asked, and refuses members that differ only in case | 2026-10-13 | accepted |
| [0052](0052-cause-on-fault-joins-2-1.md) | A `Cause` on `Fault<TFault>` joins 2.1 | 2026-10-13 | accepted |
| [0053](0053-fault-made-by-fault-of-alone.md) | A `Fault<TFault>` is made by `Fault.Of` alone | 2026-10-13 | accepted |
| [0054](0054-abstract-value-object-refused.md) | An abstract type with a value-object marker is refused (CMTK1022) | 2026-10-13 | accepted |
| [0055](0055-component-name-claimed-twice-refused.md) | A component name two types claim is refused; a qualified fault name follows the nesting chain | 2026-10-13 | accepted |
| [0056](0056-value-translated-only-through-typekit-converter.md) | `.Value` is translated only on a column TypeKit's converter stores | 2026-10-13 | accepted |
| [0057](0057-hand-written-ordering-refused-in-query.md) | A query that orders by a hand-written `CompareTo` is refused | 2026-10-13 | accepted |
| [0058](0058-integer-key-follows-ef-rule.md) | A value-object key is generated wherever EF generates its wrapped type's | 2026-10-13 | accepted |
| [0059](0059-text-guard-refuses-what-it-cannot-normalize.md) | The text guards refuse what they cannot normalize, and `AsText()` throws where nothing composes | 2026-10-13 | accepted |
| [0060](0060-value-object-over-closed-set-generated-in-full.md) | A value object over a closed set is generated in full wherever the set is declared | 2026-10-13 | accepted |
| [0061](0061-scope-s-fault-status-applies-where-fault-type-answered.md) | A scope's fault status applies where its fault type is answered | 2026-10-13 | accepted |
| [0062](0062-asynchronous-callbacks-run-where-awaited-task-completed.md) | An asynchronous callback runs where the task it waited for completed | 2026-10-13 | accepted |
| [0063](0063-char-keys-application-assigned-ulong-documented.md) | A key over `char` is the application's to assign; a default key over `ulong` throws at `Add` | 2026-10-13 | accepted |
| [0064](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md) | A fault's name reaches the client; causes it must not tell apart are one fault | 2026-10-13 | accepted |
| [0065](0065-2-0-0-ships-without-preview.md) | 2.0.0 ships without a preview | 2026-10-14 | accepted |
