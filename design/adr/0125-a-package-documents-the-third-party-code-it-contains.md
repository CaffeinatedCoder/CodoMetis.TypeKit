# 0125. A package documents the third-party code it contains, and its dependencies' licenses are checked, not listed

Date: 2026-10-30
Status: open

Context: 2026-10-30, #180. A project outside this repository ships a `THIRD-PARTY-NOTICES.TXT` generated from
its dependencies, and the question was whether the public repositories should document third-party licenses
the same way. This record is an engineering rule for this repository, not legal advice. It rests on what the
MIT and Apache-2.0 texts ask for: the notice and, for Apache-2.0, the license and any `NOTICE` travel with
copies of the licensed work that are redistributed.

## Decision

**What a package contains decides what it documents.**

1. **A package that contains only this repository's code carries no notices file.** Every package does today.
   Its license is the `PackageLicenseExpression`, its copyright the `Copyright` property, and its dependencies
   the nuspec's list, which NuGet restores with each dependency's own package and license.
2. **A package that contains third-party code carries a `THIRD-PARTY-NOTICES.txt` inside the artifact.**
   Third-party code is an assembly bundled or merged into the package, source copied into `src/`, or a binary
   or container release of an application. The file lists what the artifact contains and nothing else, so it is
   built from the shipping projects' closure alone (no test, tool or spike package), holds the license text of
   each entry rather than a link, and is regenerated in the release workflow, never by hand. Copied source also
   keeps its original header.
3. **Dependencies' licenses are checked, not listed.** A conventions test holds every package a shipping
   project resolves to an allow-list of SPDX ids, and fails a package with only a `licenseUrl` unless an
   exceptions list names it with a reason (#181). The allow-list starts at MIT, Apache-2.0, BSD-2-Clause,
   BSD-3-Clause and ISC; #181 measures what the shipping closure declares, and the list is that and no wider.
4. **No SBOM is shipped.** GitHub's dependency graph exports one (SPDX 2.3, `GET /repos/{owner}/{repo}/dependency-graph/sbom`),
   which is an inventory. GitHub's documentation says it does not retrieve license information for
   dependencies, so it is not the license record.
5. **Not adopted.** A notices file for a library that bundles nothing: it would list packages the artifact
   does not contain, test packages among them, and say nothing the nuspec does not. REUSE headers on every file:
   every file is under the one MIT license, so there is no per-file license to declare.

The other public repositories take the same rule and write it down where they keep their contributor guide.

## Evidence

- **Package contents, 2026-10-30, 2.4.0.** `dotnet pack CodoMetis.TypeKit.slnx -c Release` gives seven `.nupkg`.
  Each holds its nuspec, `README.md`, its own assembly and that assembly's XML documentation; the analyzers hold
  `CodoMetis.TypeKit.Analyzers.dll` and `CodoMetis.TypeKit.Analyzers.CodeFixes.dll` under `analyzers/dotnet/cs`.
  No third-party assembly is in any of them.
- **Dependencies.** `CodoMetis.TypeKit.Generators` depends on `Metalama.Framework` 2026.1.30 and
  `CodoMetis.TypeKit`; `CodoMetis.TypeKit` on `CodoMetis.TypeKit.Analyzers`. Each nuspec has
  `<license type="expression">MIT</license>` and `<copyright>Copyright (c) 2026 CaffeinatedCoder</copyright>`.
- **No copied code.** A search of `src/` for "adapted from", "ported from", "copied from", "derived from" and
  copyright lines finds prose only.
- **The generated file that prompted this.** 102 entries (81 MIT, 16 Apache-2.0, 2 PostgreSQL, 1 BSD-3-Clause),
  test and tooling packages (`xunit.v3`, `Moq`, `coverlet.MTP`, `Microsoft.NET.Test.Sdk`) among them, and no
  license text in it, only each SPDX id and a link to licenses.nuget.org. For an application that ships its
  dependencies, listing them is right. It would be wrong here on both counts: it would list what no package
  of ours contains, and a link is not the text.
- **GitHub's SBOM** for this repository lists 187 packages, test and tooling packages included.

## Consequences

- No file is added to any package, and nothing new is maintained, until a package starts to contain third-party
  code. Rule 2 then applies from that pull request on.
- Rule 2 has no guard yet. Nothing fails if a package starts to bundle an assembly; #181 records measuring a
  pack-contents rule beside the allow-list.
- A new package reference whose license is not allowed fails the conventions tests until the list is changed on
  purpose, in a pull request that says why.
- A README sentence that says the packages contain only TypeKit's code would be a promise, and needs a claim in
  `test/oracles/claims.json` and the test that holds it. It is not written here.
