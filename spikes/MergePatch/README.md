# Spike: can a merge-patch field tell absent from null, read a value object, and survive Native AOT?

**Status: run 2026-10-13, answered.** From the "`Patch<T>`" part of phase 13 (2.0.0) in
[the phase log](../../design/history.md).

**Question.** A JSON Merge Patch (RFC 7396) field has three states, absent, null and a value, and
System.Text.Json calls no converter for a property it does not find. Can one struct in the base
package carry the three, with `default` as absent, read a value object with its own rule, write
through `[JsonIgnore(WhenWritingDefault)]`, be described in the OpenAPI document as its argument,
and do all of it under a source-generated context and Native AOT, where a converter over an open
`T` is the hard part?

**Answer: yes, with the converter registered by the struct's own static constructor.** Measured
2026-10-13 on .NET SDK 10.0.401, Microsoft.AspNetCore.OpenApi 10.0.12, against a value object woven
by the real generators and the real OpenAPI satellite, under the reflection resolver, a
source-generated context, and published with Native AOT. Of the converter shapes, `MakeGenericType`
plus `Activator` fails under AOT ("missing native code or metadata": the closed converter type
exists, its constructor has no reflection invoker), a static field on the struct read by reflection
is trimmed, and an open generic converter type in `[JsonConverter]` is refused by the source
generator (SYSLIB1220, no metadata). A registry the closed `Patch<T>`'s static constructor fills,
which the factory runs with `RuntimeHelpers.RunClassConstructor` for the type it is asked for, passes
every claim under AOT, with one IL2059 trim warning whose reason is this measurement. Shipped in 2.0.0
as `Patch<T>` with `PatchTests`, `PatchSchemaTests` and the consumer smoke test holding each row.

## The candidate

A web project: `Candidate.cs` holds `PatchA<T>` with the three factories selectable by
`-p:Factory=Static|Registry` (the default is `MakeGenericType`), shape B behind `-p:OpenGeneric=true`,
and the schema transformer; `Model.cs` a woven `Email`, the request records and the context;
`Program.cs` runs each claim under both resolvers, then a host for the HTTP and document claims,
writing the documents to `out/`. `dotnet run -p:Factory=Registry` is the JIT run and
`dotnet publish -c Release -p:Aot=true -p:Factory=Registry -o out/aot-Registry` the AOT one.

## Findings

| # | Claim | Result |
|---|---|---|
| 1 | `default` is absent: a property not in the document reads as Absent, under both resolvers | measured: all four properties Absent, reflection and source-generated, JIT and AOT |
| 2 | null reads as Null and a value as Value, for a string, a value object, an int and a class | measured, both resolvers; the value object through its generated converter |
| 3 | A converter over an open `T` under Native AOT | measured: `MakeGenericType` + `Activator` throws "missing native code or metadata"; the static field read by reflection is null (trimmed); the registry filled by the static constructor works for every `T`, including the record-struct value object |
| 4 | T's contract under a source-generated context | measured: the generator never sees `T` behind the converter, so `string` and `int` had no metadata until the converter supplied `JsonMetadataServices`'s built-in ones; a class must be listed in the context, and the serializer's own message says so; the open generic `[JsonConverter(typeof(C<>))]` is SYSLIB1220 |
| 5 | A value `Create` refuses inside a patch is refused as anywhere else, and a bound body answers 400 | measured: `Email refused the JSON value (NoAt)`, the input not quoted; HTTP 400 problem details |
| 6 | A null for a value object is the Null state, not a refusal | measured; so `Patch<Email?>` is never needed, and a `Nullable<>` argument has no contract in a source-generated context anyway |
| 7 | Writing: Absent is left out under `WhenWritingDefault`, refused without it; Null writes null; Value writes T | measured, both resolvers, JIT and AOT |
| 8 | The document: a patch property is its argument's schema, inlined, not required, admitting null, in 3.1 and 3.0 | measured, JIT and AOT, once the reference-id hook inlines the patch, the parent's `required` drops patch properties, and a value object is described through its wrapped type (its own contract is absent under source generation) |
| 9 | CMTK0001, CMTK0005 and CMTK0006 are silent on a default, a `new()`, an array and an unassigned member of `Patch<T>` | measured: the spike's build reports no CMTK diagnostic |
| 10 | Native AOT publish of the registry shape: no trim or AOT warning in the candidate beyond IL2059 at `RunClassConstructor` | measured; the binary passes every claim but the static-field variant, which is recorded above as unviable |
