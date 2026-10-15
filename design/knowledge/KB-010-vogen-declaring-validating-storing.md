---
verified: 2026-09-28
pins:
  - external Vogen 8.0.7
  - external Vogen 9.0.0-beta.2
source: the Vogen repository at tag 8.0.7 and its nuspec on api.nuget.org, and the 9.0.0-beta.2 tag, read by a research agent
turned-on: the Vogen column of the value-object table and the rows of "Choose something else when" that name Vogen in docs/comparison.md
test: none, a competitor's feature, read from its source and package
---

# KB-010. Vogen 8.0.7 declares a value object by attribute, validates through `Validate`, and targets .NET Standard 2.0

Read at the release tag and in the package:

- A value object is `[ValueObject<T>]` on a partial type.
- `Validate` returns a `Validation` (`Validation.Ok` or `Validation.Invalid`); `From` throws on an invalid value.
- VOG009 and VOG010, both errors, refuse `default` and `new` of a value object, so refusing `default` at compile
  time is not TypeKit's alone. VOG001 is "types cannot be nested", not a rule against a value object wrapping
  another.
- EF Core: a converter generated per value object, which you register. Comparing a value object with a primitive
  in a query is an error (VOG034).
- The package targets `netstandard2.0`, so .NET Framework projects can use it. License Apache-2.0.
- Opt-in integrations for Newtonsoft.Json, Dapper, LINQ to DB, MongoDB, Orleans, ServiceStack and protobuf-net.

The 9.0.0-beta.2 tag (2026-07-21) changes none of this. Its OpenAPI and Native AOT facts are [KB-011](KB-011-vogen-openapi-and-native-aot.md).
