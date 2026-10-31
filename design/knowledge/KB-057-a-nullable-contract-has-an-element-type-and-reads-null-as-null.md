---
verified: 2026-10-31
pins:
  - Microsoft.AspNetCore.OpenApi 10.0.12
  - sdk 10.0.4xx
source: System.Text.Json in the .NET 10 runtime, the contract of a Nullable<T> from the reflection resolver and a JSON null read into T? as a property, an element and the root, over a T whose converter handles null, measured; and the schemas Microsoft.AspNetCore.OpenApi hands its schema transformers below a Nullable<Patch<int[]>>, measured in PatchSchemaTests
turned-on: the fix of issue 232 (pull request 237), where a nullable patch is described as its argument admitting null but not as a merge-patch field, the container check reads the kind before the element type, and the argument's elements below a nullable patch are skipped
test: test/CodoMetis.TypeKit.Facts/KB057.cs
---

# KB-057. A `Nullable<T>` contract has `T` as its element type, and a JSON null read into it is null whatever `T`'s converter does

The contract System.Text.Json gives a `Nullable<T>`:

- has `Kind` `None` and `ElementType` `T`, for a built-in `T` and one with a converter of its own; `T`'s own
  contract has no element type;
- reads a JSON null as null, without calling `T`'s converter, even one whose `HandleNull` is true. That holds as a
  property, as an element (`List<T?>`) and at the root. A plain `T` hands the same null to its converter.

So a `T?` property the request sets to null reads the same as one it leaves out. For `Patch<T>?` that means a
null is never the `Null` state, and a request can't clear the field through it.

Microsoft.AspNetCore.OpenApi walks a schema's `items` and `additionalProperties` with the contract's element type,
whatever its kind. Below a nullable schema it hands them to every schema transformer as `T`: the numbers of a
`Patch<int[]>?` reached the patch transformer as a `Patch<int[]>` and gained a null. `PatchSchemaTests` holds that
consequence (its guard's seeds), and this entry's test the serializer's part.
