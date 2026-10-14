# Spike: can a generated member carry XML documentation, on Metalama 2026.1?

**Status: run 2026-10-13, answered: no.** Asked for 2.0.0: the generated members (`From`, `TryFrom`,
`FromKnownGood`, `Parse`, `TryParse`, `Value`, `CompareTo`, the operators, a closed set's `Match`,
`All`, `Name`) carry no XML documentation, so IntelliSense shows nothing for them.

**Question.** Does a member a Metalama aspect introduces carry the XML documentation comment of its
template, or can an aspect give it one, and does that documentation reach (a) the consumer's compiled
XML documentation file, which a referencing project's IntelliSense reads, and (b) the IDE's
design-time view of the consumer's own project?

**Answer: neither, on Metalama 2026.1.29 nor on 2027.0.3-preview (SDK 10.0.401).** A template's
documentation comment is not copied to the member introduced from it, whether introduced
declaratively (`[Introduce]`) or programmatically (`IntroduceMethod`, `IntroduceProperty`), and the
framework has no API that sets one: the public API of both versions, searched in its XML
documentation, has no member about documentation, and the only comment API, `meta.InsertComment`,
writes a `//` comment into a body. The consumer's documentation file lists the members written by hand
and none of the introduced ones. Supporting it would need either Metalama to copy or accept
documentation, or a step of our own that rewrites the consumer's documentation file after the build;
the second is a hand-rolled workaround that would also miss the IDE's view of the consumer's own
project, so it is not done.

## The candidate

`Spike.Aspects` holds `DocumentedAspect`, which introduces a method and a property declaratively and
a method, a property and a nested class programmatically, each template with a `///` comment.
`Spike.App` applies it to a documented `Target`, with `GenerateDocumentationFile` and
`MetalamaEmitCompilerTransformedFiles`. `dotnet build IntroducedMemberDocs.slnx`, then read
`Spike.App/bin/Debug/net10.0/Spike.App.xml` and `Spike.App/obj/Debug/net10.0/metalama/Target.cs`.
`-p:MetalamaVersion=2027.0.3-preview` measures the preview.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | The woven `Target` holds the four introduced members and the nested class | measured: all five, in `obj/.../metalama/Target.cs` |
| 2 | An introduced member carries its template's `///` comment | measured: no; the only `///` in the woven file is the hand-written one on `Target` |
| 3 | The consumer's XML documentation file documents an introduced member | measured: no; one `<member>`, `T:Spike.App.Target` |
| 4 | An aspect can set a member's documentation | no such API in Metalama.Framework 2026.1.29 or 2027.0.3-preview (their XML documentation has no member about documentation; `meta.InsertComment` writes a `//` comment in a body) |
| 5 | Metalama 2027.0.3-preview copies the template's comment | measured: no, as 2 and 3 |
| 6 | An undocumented introduced public member is a CS1591 in a consumer with `GenerateDocumentationFile` | measured: no warning at all, so a consumer that treats warnings as errors builds |
| 7 | The IDE's design-time view shows documentation for an introduced member | not measured: the design-time pipeline runs in the IDE's process, not in a command-line build. With no documentation at compile time and no API to give one, nothing suggests it would |
| 8 | It changes when the aspect library writes its own documentation file (`-p:GenerateDocumentationFile=true`) | measured: no; the library's file documents its five templates, the consumer's still one member |
| 9 | The same holds for this repository's generators, whose templates are documented and whose project writes a documentation file | measured in the woven probes (`test/CodoMetis.TypeKit.Generators.Probes/obj/.../metalama/Probes.cs`): `From` has none of its template's "Wraps any value but null", a closed set's `Parse` none of "A FormatException naming the set" |

## What it decided

Generated members stay undocumented in 2.0.0. The roadmap's 2.1 line: ask Metalama for documentation
on introduced declarations (copied from the template, or set through the builder), and document every
generated member once it exists, with a test that reads a consumer's documentation file.
