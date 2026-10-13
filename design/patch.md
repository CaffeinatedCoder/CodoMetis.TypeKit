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
