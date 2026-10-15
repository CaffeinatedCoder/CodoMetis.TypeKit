# Verified facts

What the frameworks, platforms and tools TypeKit builds on do, as this repository verified it: one fact per
file, each pinned to the versions it was checked on, dated, with the primary source it was checked against and
what here turned on it ([decision 93](../adr/0093-verified-facts-are-pinned-dated-and-executable-where-they-can-be.md)).
A fact a test can observe has a characterization test in `test/CodoMetis.TypeKit.Facts`, named by its id, which
fails when a dependency changes the behaviour.

Look here before researching a framework question: `docs-query entry KB-NNN`, or `docs-query find <words>`. A fact
whose pin no longer matches `Directory.Packages.props` or `global.json` is stale and settles nothing until it is
re-verified; `docs-query check` lists the stale ones.

**Adding one.** Only what something here turned on (a decision, a fix, a probe's verdict), pinned and
falsifiable, checked against a primary source. Take the next number, copy an entry's frontmatter, write the test
first where a test can observe it, and add a row below.

| Id | Fact | Pins | Test |
|---|---|---|---|
| [KB-001](KB-001-openapi-2-nullable-body-drops-pattern.md) | Under OpenAPI 2.0, ASP.NET writes a nullable request body as `allOf: [{x-nullable: true}]` and drops its `pattern` | OpenApi 10.0.12, SDK 10.0.4xx | `KB001` |
| [KB-002](KB-002-api-explorer-lists-a-controller-only-when-visible.md) | An MVC controller is in the OpenAPI document only when ASP.NET's API explorer sees it | OpenApi 10.0.12, SDK 10.0.4xx | `KB002` |
| [KB-003](KB-003-typedescriptor-described-before-registered.md) | A type `TypeDescriptor.GetConverter` described first can no longer be registered | SDK 10.0.4xx | `KB003` |
| [KB-004](KB-004-windows-refuses-a-replace-over-a-held-file.md) | On Windows, a rename that replaces a file someone holds open is refused, but moving it aside is allowed | SDK 10.0.4xx | `KB004` |
| [KB-005](KB-005-launch-costs-of-the-tool-forms.md) | `dotnet <built dll>` starts in about 0.02 s, `dotnet run --project` in 0.7 to 0.9 s | SDK 10.0.4xx | prose |
| [KB-006](KB-006-nested-serializer-call-restarts-reference-ids.md) | Under `ReferenceHandler.Preserve`, a nested `JsonSerializer` call numbers its references from 1 again | SDK 10.0.4xx | `KB006` |
| [KB-007](KB-007-a-resolver-s-contract-wins-over-the-options-converters.md) | A resolver of one's own decides a type's contract over a converter in the options, and `T?` follows `T` | SDK 10.0.4xx | `KB007` |
| [KB-008](KB-008-the-nullable-adapter-is-the-serializer-s-own.md) | A `Nullable<T>` contract's converter is System.Text.Json's own adapter, and nothing public names what it wraps | SDK 10.0.4xx | `KB008` |
| [KB-009](KB-009-a-modifier-s-converter-wins-over-the-attribute.md) | A resolver modifier's `CustomConverter` writes a property over its `[JsonConverter]` | SDK 10.0.4xx | `KB009` |
