# Patch

`Patch<T>` is one field of a JSON Merge Patch (RFC 7396): absent, null or a value. It is in the base
package, is the one wire type there, and is hand-written, not generated. Measured in
[spikes/MergePatch](../spikes/MergePatch/README.md); shipped in 2.0.0
([decision 42](adr/0042-2-0-0-next-release-bundles.md)). The consumer's guide is
[docs/merge-patches.md](../docs/merge-patches.md).

## What must stay true

- **`default` is Absent.** System.Text.Json calls no converter for a property it does not find, so
  the state a missing property leaves is the struct's `default`. It is the one `default` in the
  package that means something, so CMTK0001, CMTK0005 and CMTK0006 leave `Patch<T>` alone
  (`PatchIsDefaultableTests`).
- **A wire type beside `Option`, which is not one** ([decision 15](adr/0015-option-result-wire-types.md)):
  an option says whether a value exists, a patch whether a request mentioned a field. `ToOption()`
  crosses from one to the other, and nothing converts silently.
- **Reading never refuses a null.** Null is a state of the wire format whatever `T` is; whether a
  clear is accepted is the application's rule, said in `Match`. A value object inside a patch is
  read by its own converter, so its rule applies there as anywhere else.
- **A refusal is at the patched property's path.** The converter reads the value with a nested
  `JsonSerializer.Deserialize`, which sets the path within that value, `$`, and the serializer keeps
  a path it finds set, so a client was told `$` for a bad field. The converter throws the refusal
  again without a path, and the serializer sets the property's, `$.quantity` or `$[1].quantity`, as
  for a property of `T`. The nested sentence is kept, a value object's own refusal among them, less
  the location the serializer appended to its default sentence, which named `$`. Inside a patched
  object the path ends at the patched property, since a converter cannot extend the serializer's
  path (`PatchTests`).
- **`T` is `notnull`**, as `Option<T>`'s is: a value is never null, a clear being the Null state.
  Unconstrained, a `Patch<string?>` handed its value to `Match`, `Tap`, `Map` and `Traverse` as a
  `string?`, so `p => p.Trim()` warned (CS8602) and `Traverse(Email.Create)` did not match (CS8622).
  Handing the non-null `T` to a `Patch<string?>` and a `Patch<string>` alike cannot be written
  (measured 2026-10-13): overload resolution ignores nullability, so one member serves both; over
  `Patch<T?> where T : class` a `Patch<string>` receiver is CS8620, two blocks over `Patch<T>` and
  `Patch<T?>` lower to one signature (CS0111), and `[DisallowNull]` on a delegate's parameter does not
  reach a lambda's (CS8602, CS8622). So the declaration is warned instead, once (CS8714), and a
  `Patch<int?>`, which has no contract under a source-generated context either, with it
  (`PatchNullabilityTests`).
- **Writing an absent patch is refused**, since a converter cannot leave a property out;
  `[JsonIgnore(Condition = WhenWritingDefault)]` leaves it out instead.
- **`ToString()` never prints the value**, as for `Option` and `Result`.

## The converter, and Native AOT

A converter over an open `T` is the hard part. `MakeGenericType` plus `Activator` has no code under
Native AOT, a static field read by reflection is trimmed, and an open generic `[JsonConverter]` is
refused by the source generator (SYSLIB1220), all measured. So each closed `Patch<T>`'s static
constructor registers its converter with `PatchJsonConverterFactory`, which runs the constructor
with `RuntimeHelpers.RunClassConstructor` for the type it is asked for. That call is the one
`[UnconditionalSuppressMessage]` (IL2059), its reason the spike's measurement and the smoke test's
`aot` consumer. The registry is a `ConcurrentDictionary`.

Under a source-generated context the generator never sees `T` behind the converter: a primitive, a
string, a `Guid`, a date or a `Uri` gets the serializer's built-in contract, a value object its own
converter, and anything else must be listed in the context, where the serializer's own message
names what is missing.

## Where each satellite meets a patch

| Where | How | Held by |
|---|---|---|
| OpenAPI | The argument's schema, inlined, admitting null, never required, in 3.1 and 3.0 | `PatchSchemaTests` |
| Testing | Assertions per state that hand back the value | `PatchAssertionTests` |
| Serilog | Logs its state only | the Serilog tests |
| EF Core | Not mapped: a patch is the shape of a request, applied to an entity with its own types | — |

## Not in scope

A deep merge (`Patch<Address>` replaces the address; nested patches are `Patch<AddressPatch>`), and
ASP.NET Core's JSON Patch (RFC 6902), which is a list of operations.
