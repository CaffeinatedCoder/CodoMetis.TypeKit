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
falsifiable, checked against a primary source. A competitor's feature is a fact too, pinned
`external {name} {version}`: `check` never compares it, so it is as fresh as its date, and `docs-query due --nuget`
lists the ones to re-read before a release (CONTRIBUTING, "Releasing"): verified more than six months ago, or pinned
to a package NuGet has a later release of. Take the next number, copy an
entry's frontmatter, write the test first where a test can observe it, and add a row below.

**A fact that prunes or excuses is executable** ([decision 107](../adr/0107-a-cited-fact-is-executable-and-seeds-run-by-footprint.md)).
A `refused` entry of the axes file, a `not-promised` entry, an obligation's declaration and the probe harness may
cite only a fact with a test; `docs-query check` fails `fact-not-executable-cited` otherwise. A fact without one
explains, and decides no row.

**A measured answer that is data is a fixture.** Where the fact's answer is a document, a status or an exception at an
operation, it is `test/CodoMetis.TypeKit.Facts/KBNNN.fixture.json` beside the test, named by a `fixture:` line after
`test:`: the fact's id and its `answers`, each an `id`, a `kind` (`document` with `at`, a JSON pointer; `status`;
`exception`), the settings it was measured under (`when`, axis to value as a row names them), the `value` and a
`note`. The test is a theory over the fixture's answers, measuring each (`KB001`), and `FactFixture` reads it, for the
test and later for the harness's `Baseline.Fact`.

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
| [KB-010](KB-010-vogen-declaring-validating-storing.md) | Vogen 8.0.7 declares a value object by attribute, validates through `Validate`, and targets .NET Standard 2.0 | Vogen 8.0.7, 9.0.0-beta.2 | prose |
| [KB-011](KB-011-vogen-openapi-and-native-aot.md) | Vogen 8.0.7 generates OpenAPI schemas only on request, from a built-in type table, and does not declare Native AOT compatibility | Vogen 8.0.7, 9.0.0-beta.2 | prose |
| [KB-012](KB-012-thinktecture-value-objects.md) | Thinktecture.Runtime.Extensions 10.6.0 declares value objects, smart enums and unions by attribute, for net8.0 and net9.0 | Thinktecture 10.6.0 | prose |
| [KB-013](KB-013-thinktecture-openapi-and-native-aot.md) | Thinktecture.Runtime.Extensions 10.6.0 documents OpenAPI through Swashbuckle alone, and does not declare Native AOT compatibility | Thinktecture 10.6.0 | prose |
| [KB-014](KB-014-stronglytypedid.md) | StronglyTypedId 1.0.0-beta08 is an attribute on a partial struct, with no validation, analyzers or OpenAPI support | StronglyTypedId 1.0.0-beta08 | prose |
| [KB-015](KB-015-languageext-option.md) | LanguageExt has no public `.Value` on `Option`, and its `default` is `None`, in 4.4.9 and the 5.0 beta | LanguageExt 4.4.9, 5.0.0-beta-78 | prose |
| [KB-016](KB-016-csharpfunctionalextensions.md) | CSharpFunctionalExtensions 3.7.0 throws from `.Value` on a failure, and its `default(Result<T>)` is a success | CSharpFunctionalExtensions 3.7.0 | prose |
| [KB-017](KB-017-erroror.md) | ErrorOr 2.1.1 returns `default` from `.Value` on an error, and its `default` instance is a success | ErrorOr 2.1.1 | prose |
| [KB-018](KB-018-fluentresults.md) | FluentResults 4.0.0 results are classes that carry reasons, errors and successes, and an error can name its cause | FluentResults 4.0.0 | prose |
| [KB-019](KB-019-oneof.md) | OneOf 3.0.271 is a readonly struct whose `default` is its first case | OneOf 3.0.271 | prose |
| [KB-020](KB-020-ardalis-smartenum.md) | Ardalis.SmartEnum 8.2.0 is a run-time base class, finds its members by reflection, and requires no member in `When` | Ardalis.SmartEnum 8.2.0 | prose |
| [KB-021](KB-021-intellenum.md) | Intellenum 2.0.1 has one underlying value per member, and its case-insensitive lookup answers the first declared | Intellenum 2.0.1 | prose |
| [KB-022](KB-022-thinktecture-smart-enums.md) | Thinktecture.Runtime.Extensions 10.6.0 smart enums have a key apart from the identifier, a `Switch` over every member, and look keys up ignoring case | Thinktecture 10.6.0 | prose |
| [KB-023](KB-023-a-converter-cannot-describe-its-schema.md) | A JSON converter of one's own cannot say what it writes: the schema exporter writes the empty schema, ASP.NET only a format | OpenApi 10.0.12, SDK 10.0.4xx | `KB023` |
| [KB-024](KB-024-no-library-constrains-type-arguments-by-kind.md) | No .NET library constrains a type argument to be abstract, sealed, a record or concrete, or two arguments to differ | AdvancedGenericTypeConstraints 0.4.1, RossLean.GenericsAnalyzer 1.4.0 | prose |
| [KB-025](KB-025-the-coverage-collector-rewrites-assemblies-on-disk.md) | The code coverage collector instruments the assemblies on disk, so test hosts started together from one output directory corrupt each other, and its in-memory mode records no coverage on macOS arm64 | Microsoft.Testing.Extensions.CodeCoverage 18.12.0, SDK 10.0.4xx | prose |
| [KB-026](KB-026-openapi-2-nullable-component-property-loses-its-reference.md) | Under OpenAPI 2.0, ASP.NET writes a nullable property of a component as `allOf: [{x-nullable: true}]`, without the reference | OpenApi 10.0.12, SDK 10.0.4xx | `KB026` |
| [KB-027](KB-027-an-enum-component-is-its-first-use.md) | The component ASP.NET makes for an enum written by name is the schema of the first use it meets, null included | OpenApi 10.0.12, SDK 10.0.4xx | `KB027` |
| [KB-028](KB-028-postgresql-nan-is-greater-than-every-number.md) | PostgreSQL treats NaN as greater than every number, so `> 5` keeps a NaN row and NaN sorts after Infinity | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB028`, with `TYPEKIT_PG` |
| [KB-029](KB-029-npgsql-truncates-ticks-below-a-microsecond.md) | Npgsql truncates ticks below a microsecond, for every date and time type, and does not round them | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB029`, with `TYPEKIT_PG` |
| [KB-030](KB-030-datetime-maxvalue-reads-back-unspecified.md) | `DateTime.MaxValue` written to `timestamptz` as UTC reads back with `Kind` Unspecified | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB030`, with `TYPEKIT_PG` |
| [KB-031](KB-031-a-space-in-character-1-disappears-as-text.md) | A space in `character(1)` disappears when SQL converts it to text | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB031`, with `TYPEKIT_PG` |
| [KB-032](KB-032-postgresql-sorts-null-last-ascending.md) | PostgreSQL sorts NULL last ascending and first descending, and EF adds no `NULLS FIRST` | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB032`, with `TYPEKIT_PG` |
| [KB-033](KB-033-startswith-is-case-sensitive-and-order-follows-the-collation.md) | On PostgreSQL `StartsWith` is case-sensitive, and ordering follows the database collation, not ordinal order | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB033`, with `TYPEKIT_PG` |
| [KB-034](KB-034-numeric-p-s-rounds-on-write.md) | `numeric(p,s)` rounds a value with more fractional digits on write | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB034`, with `TYPEKIT_PG` |
| [KB-035](KB-035-npgsql-refuses-non-utc-for-timestamptz.md) | Npgsql refuses a non-UTC `DateTime` and a non-zero-offset `DateTimeOffset` for `timestamptz` | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB035`, with `TYPEKIT_PG` |
| [KB-036](KB-036-a-char-key-gets-an-identity-postgresql-refuses.md) | A plain `char` key gets an identity column on Npgsql, which PostgreSQL refuses | PostgreSQL 17.11, Npgsql EF 10.0.3, EF Core 10.0.12 | `KB036`, with `TYPEKIT_PG` |
| [KB-037](KB-037-mvc-under-max-validation-depth-1-runs-an-unbound-action.md) | Under `MaxValidationDepth = 1`, MVC runs an action whose parameter it could not bind, with the parameter at its default | SDK 10.0.4xx | `KB037` |
| [KB-038](KB-038-a-byte-array-is-a-base64-string.md) | System.Text.Json writes a `byte[]` as one base64 string, and every other collection as a JSON array | SDK 10.0.4xx | `KB038` |
| [KB-039](KB-039-sqlite-neither-compares-nor-orders-datetimeoffset-timespan-or-ulong.md) | EF Core's SQLite provider neither compares nor orders a `DateTimeOffset`, a `TimeSpan` or a `ulong`, and does both for a `decimal` | EF Core 10.0.12, its SQLite provider 10.0.12 | `KB039` |
| [KB-040](KB-040-a-converter-is-charged-a-level-of-max-depth.md) | Under `MaxDepth`, System.Text.Json refuses a value a converter of one's own writes at the limit, where its own converter writes it | SDK 10.0.4xx | `KB040` |
| [KB-041](KB-041-mvc-cannot-write-its-400-under-a-context-without-validation-problem-details.md) | MVC cannot write its automatic 400 under a source-generated context that does not list `ValidationProblemDetails` | SDK 10.0.4xx | `KB041` |
| [KB-042](KB-042-a-char-schema-is-refused-under-write-as-string.md) | Under `NumberHandling` with `WriteAsString`, ASP.NET refuses the OpenAPI document of an endpoint whose schema holds a `char` | OpenApi 10.0.12, SDK 10.0.4xx | `KB042` |
| [KB-043](KB-043-github-actions-skips-concurrency-and-dispatch.md) | On GitHub Actions, a skipped job passes a required check, concurrency groups ignore case and a called workflow takes its caller's `github.workflow`, and the GITHUB_TOKEN can dispatch a workflow | external GitHub-Actions 2026-10-29 | prose |
| [KB-044](KB-044-superstrong-types.md) | SuperStrong.Types 1.0.0 is an attribute on a partial class, validated everywhere it creates a value, with no Native AOT declaration | SuperStrong.Types 1.0.0 | prose |
| [KB-045](KB-045-first-releases-of-the-compared-libraries.md) | The compared libraries were first published on NuGet between 2015 and 2026 | NuGet, 2026-10-29 | prose |
| [KB-046](KB-046-csharp-15-unions-ship-with-the-net-11-sdk.md) | C# 15's union types and closed hierarchies need no preview flag in the .NET 11 SDK, which is still prerelease | .NET 11 SDK RC 2 | prose |
| [KB-047](KB-047-a-converter-s-hints-factory-comes-after-hilo.md) | EF Core consults a converter's mapping-hints value generator after a property's generator and after HiLo, and gives a key that is no number the temporary number of its column | EF Core 10.0.12, Npgsql 10.0.3 | `KB047` |
| [KB-048](KB-048-sql-server-compares-text-ignoring-case.md) | Under SQL Server's default collation text compares and orders ignoring case and trailing spaces: `==`, `IN` and `LIKE` match rows that differ in case | SQL Server 2022, EF SqlServer 10.0.12 | `KB048`, with Docker |
| [KB-049](KB-049-sql-server-orders-a-uniqueidentifier-by-its-last-group.md) | SQL Server orders a `uniqueidentifier` by its last group first, where `Guid.CompareTo` starts at the first | SQL Server 2022, EF SqlServer 10.0.12 | `KB049`, with Docker |
| [KB-050](KB-050-a-decimal-is-decimal-18-2-on-sql-server.md) | A `decimal` is a `decimal(18,2)` on SQL Server: every value reads back at scale 2, more digits rounded on write | SQL Server 2022, EF SqlServer 10.0.12 | `KB050`, with Docker |
| [KB-051](KB-051-sql-server-s-time-holds-less-than-a-day.md) | SQL Server's `time` holds less than a day: a longer `TimeSpan` is refused in a column, and read by a query as another value from JSON | SQL Server 2022, EF SqlServer 10.0.12 | `KB051`, with Docker |
| [KB-052](KB-052-sql-server-takes-no-max-of-a-bit.md) | SQL Server takes neither `MAX` nor `MIN` of a `bit` | SQL Server 2022, EF SqlServer 10.0.12 | `KB052`, with Docker |
| [KB-053](KB-053-greatest-and-least-need-compatibility-level-160.md) | EF translates `Greatest` and `Least` on SQL Server only from compatibility level 160 | EF SqlServer 10.0.12 | `KB053` |
| [KB-054](KB-054-datetime2-keeps-every-tick-and-forgets-the-kind.md) | `datetime2` keeps every tick of a `DateTime` and reads it back with `Kind` Unspecified | SQL Server 2022, EF SqlServer 10.0.12 | `KB054`, with Docker |
| [KB-055](KB-055-ericksonlopez-domainprimitives.md) | EricksonLopez.DomainPrimitives 3.0.0 is an attribute on a partial record struct, with a catalogue of ready-made types, re-validated on every read | EricksonLopez.DomainPrimitives 3.0.0 | prose |
| [KB-057](KB-057-a-nullable-contract-has-an-element-type-and-reads-null-as-null.md) | A `Nullable<T>` contract has `T` as its element type, and a JSON null read into it is null whatever `T`'s converter does | OpenApi 10.0.12, SDK 10.0.4xx | `KB057` |
| [KB-058](KB-058-ef-orders-captured-values-in-sql-only-with-a-lambda.md) | EF orders captured values in SQL only when the chain takes a lambda: `values.Max()` runs in memory, `values.Max(v => v)` in SQL | EF Sqlite 10.0.12 | `KB058` |
