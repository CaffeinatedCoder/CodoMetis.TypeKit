# 0118. An Error may learn, in a minor version, a form only that version's new API can write

Date: 2026-10-29
Status: accepted

Context: 2026-10-29, issue #153, found by the pre-release sweep of `release/2.3.0` at `99c6032`; refines
[decision 34](0034-errors-fixed-within-major-version-warnings.md). 2.3.0 gives every assertion of
`CodoMetis.TypeKit.Testing` a form on a `Task` or `ValueTask` of what it checks (#87), and CMTK0003, an
Error since 2.0.0 ([decision 50](0050-warnings-cmtk0003-alone-becomes-error-2.md)), reports such an
assertion left without `await` (`IgnoredOutcomeAnalyzer.IsPendingAssertion`): it fails the test only through
the task it returns, so the dropped task passes whatever the value is. Decision 34 says an Error that
reports more waits for a major version, and the Analyzers README promised the same, so the 2.3.0 analyzer
contradicted the 2.3.0 README with no record of why.

## Decision

**An Error may report, in a minor version, a form that only an API new in that same version can write.**
Decision 34 keeps Errors fixed within a major so that an update never fails a build that worked before.
A form that needs the new API to be written cannot occur in code that built on the previous version, so
reporting it fails no such build. The rule holds when every reported form calls the new API itself; a
form that merely sits beside it, or that a name alone recognises, does not qualify.

**What qualifies in 2.3.0.** CMTK0003's dropped pending assertion: an expression statement that drops,
without `await`, the task an assertion of `CodoMetis.TypeKit.Testing` returns on a `Task` or `ValueTask`.
The rule knows the assertion by the package's own assembly (`TypeKitSymbols.IsTestingAssembly`), so a
consumer's extension of the same name on a pending result, which may have built on 2.2.0, stays silent
(`IgnoredOutcomeAnalyzerTests.A_task_of_another_assembly_s_extension_on_a_pending_value_is_silent`). No
preview of 2.3.0 was published (nuget.org lists 2.2.0 as the latest of both packages on 2026-10-29), so
no build on these assertions exists before the tag.

**It covers #152's forms.** The same assertions passed through `ConfigureAwait` or `WaitAsync` of a plain
`Task`, and reached through a branch of a `?:`, a `??` or a `switch` expression, are written only with
the same new API, so their reach widens before the tag under this decision.

**The forms freeze at the tag.** Once 2.3.0 ships, its API is no longer new: a further form of these
assertions, or of any other 2.3.0 API, that CMTK0003 learns afterwards waits for a major version, as
decision 34 says. A Warning or an Info may still learn more forms in any minor version.

**Decision 34 stands otherwise.** A new rule ships at Warning or Info in a minor version, and an Error
that reports more of the code a consumer could already write waits for a major. The Analyzers README's
"What a new version may change" states both, and claim `T01-an-error-learns-only-new-api-forms` in
`test/oracles/claims.json` quotes the exception.
