# Security policy

## Reporting a vulnerability

**Please do not open a public issue for security reports.**

Report privately through GitHub:

> **[Report a vulnerability](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/security/advisories/new)**
> (Security → Advisories → Report a vulnerability)

If you cannot use GitHub advisories, email **meistercoder@mr-gross.de** with `SECURITY` in the
subject.

Helpful to include, as far as you have it:

- The package and version (`CodoMetis.TypeKit`, `.Analyzers`, `.Generators`,
  `.EntityFrameworkCore`, `.AspNetCore`)
- The value object declaration involved, and whether the value came from application code or from
  parsed input (JSON, route, query string, database)
- A minimal reproduction
- What you expected instead
- Why you consider it security-relevant, i.e. the impact you have in mind

## What to expect

This is a single-maintainer, non-commercial open-source project. There is no SLA, and the
following is a good-faith intention rather than a guarantee:

| | |
|---|---|
| Acknowledgement | within 7 days |
| Initial assessment | within 30 days |
| Fix or documented decision not to fix | best effort, tracked in the advisory |

## Disclosure

Coordinated disclosure. The intent is to publish a fix and a GitHub Security Advisory together,
and to keep the report private until then. Ninety days from acknowledgement is the default ceiling
for going public, whatever the fix status. Reporters are credited unless they ask not to be.

## Supported versions

No version has been released yet. Once one is, security fixes land on the latest released minor
version.

### If this project stops being maintained

This is a single-maintainer project, and that is the honest continuity risk. The signal would be
unambiguous: the repository archived, the packages marked deprecated on nuget.org, and a note here.
Published versions stay on nuget.org regardless. The code is MIT-licensed and the release path needs
nothing but this repository, so forking is the intended continuity mechanism, not a fallback.

## How releases are made

There is one publishing path, `.github/workflows/release.yml`, run by pushing a `v*` tag:

- **No stored credential.** Packages are pushed through nuget.org Trusted Publishing: GitHub signs a
  short-lived token for that workflow in this repository, and nuget.org exchanges it for a key valid
  for one hour. No API key exists in the repository or its secrets.
- **Tested before anyone approves.** The full test suite and a smoke test that installs the packed
  packages into throwaway projects run unattended. Only then does the push wait for a maintainer's
  approval, and the job that can mint the token can neither build nor write to the repository.
- **What was tested is what ships.** The push downloads the packages the verify job built; nothing
  is rebuilt after the tests.
- **An SBOM per package.** Each GitHub release carries a CycloneDX SBOM for every package, listing
  what a consumer of that package actually receives. Build-only references (the analyzer's Roslyn
  compiler packages, which run inside your own compiler) are excluded, and a test keeps that list
  complete.
- **Source and symbols.** Every package has Source Link and a symbol package, so the code you step
  into is the code that was built.
- **No license needed.** Metalama builds without a license; CI and the release run without one.

## Where this package sits

**Part of this package family runs inside your build, and the rest in your request path.**

1. **Build time.** `CodoMetis.TypeKit.Generators` runs Metalama aspects, and
   `CodoMetis.TypeKit.Analyzers` (which arrives with `CodoMetis.TypeKit`) runs Roslyn analyzers,
   inside your compiler process. Referencing `.Generators` makes `Metalama.Framework` flow into
   every project that references yours. `CodoMetis.TypeKit` alone brings no Metalama.
2. **Validation.** `IValidatedValue` types are often used as the boundary check on untrusted input.
   The generated `TryFrom`, JSON converter, `IParsable` and `TypeConverter` must all apply the
   same `Create` rules.
3. **Materialisation.** Two paths rebuild value objects **without** validation, by design, and
   trust that the application wrote what they read: the EF Core satellite for columns, and
   `StoredJsonConverterFactory` for JSON the application stored itself, where it is registered on
   that store's `JsonSerializerOptions` and nowhere else.

## In scope

- **A generated entry point that bypasses validation.** A JSON converter, `Parse`, `TryParse`,
  model binder, type converter or `default` path that produces an `IValidatedValue` instance
  `Create` would have refused.
- **An `Option` or `Result` that reads as success when it is not**, such as a `default` instance
  that passes the analyzer, a serialization path that writes one as `{}` or reads one back as
  `default` without raising, or any path that exposes the value of a `None` or an error.
- **A validation-free path reachable from input.** The materializer, the stored-JSON mode of the
  generated converter, or anything else that skips `Create`, becoming reachable other than through
  the EF satellite or an explicitly registered `StoredJsonConverterFactory`.
- **An analyzer that silently stops enforcing its rule** where it is the only guard against
  `default(T)` of a validated value.
- **Denial of service through parsing or JSON** out of proportion to input length.
- **Supply-chain integrity issues** in the packages or the release workflow.

## Out of scope

- Vulnerabilities in Metalama, EF Core, ASP.NET Core or Roslyn themselves. Report those upstream.
- Rules you wrote in your own `Create`. The package guarantees they are applied consistently, not
  that they are sufficient.
- Anything that fails loudly: a refused value, a `FormatException`, a build error.
- Values the application itself wrote and reads back, from a column or from a store whose options
  register `StoredJsonConverterFactory`: materialisation trusts them by contract. Registering the
  factory on options that read input is a misuse, not a vulnerability in the package.
