# Native AOT

**Decided 2026-09-27 ([decision 16](adr/0016-native-aot-wherever-platform-allows.md)), measured by publishing consumers with Native AOT** (SDK 10.0.401,
ILCompiler 10.0.12, osx-arm64), JSON through source-generated contexts, OpenAPI in a slim web
host, EF Core through `dotnet ef dbcontext optimize --precompile-queries --nativeaot`.

- **What the analyzers can and cannot see.** `IsAotCompatible` runs the trim and AOT analyzers on
  the packages' own code, and warnings are errors. The generators' product is compiled in the
  consumer, where only the consumer's publish sees it, and the analyzers cannot tell that a run-time
  path still works: the two worst findings (stored JSON validated, every schema `{}`) produced no
  warning at build time on the JIT. So the smoke test publishes with Native AOT and runs the binary.
- **Trimming removes interfaces nothing uses.** ILCompiler keeps an interface on a type only if the
  interface's definition is used somewhere; `typeof(IValueObject<,>)` does not count. Neither
  `[DynamicallyAccessedMembers(Interfaces)]` on the interface nor on the value object kept it (both
  measured). Custom attributes are kept, which is why discovery reads
  `GeneratedValueObjectAttribute<,>` ([decision 17](adr/0017-run-time-discovery-reads-generatedvalueobjectattribute-tvalueobject.md)).
- **And keeps them once anything uses the interface.** The attribute's own constraint does, and so
  does the EF satellite's converter: with the attribute generated, a `GetInterfaces()` lookup found
  `IValueObject<,>` again under Native AOT, and without it the same host described every value object
  as `{}` (both measured). That is ILCompiler's behaviour, not a contract, so the smoke test cannot
  catch a return to `GetInterfaces()` at run time; the analyzers' IL2070, an error in the packages'
  build, does.
- **JSON.** The generated converter's wrapped value goes through `GeneratedJson.TypeInfo` ([Aspects](aspects.md)), so a
  context lists the value objects and never what they wrap. A value object wrapping a type the
  serializer has no converter for (a class of the application's) still needs that type in the
  context, and the serializer's refusal names it.
- **OpenAPI.** The host's context lists what its value objects wrap ([OpenAPI](openapi.md)).
- **EF Core** is experimental under Native AOT and needs a compiled model and precompiled queries;
  its `DbContext` constructors require dynamic code. With the satellite, a value object maps, writes,
  queries by `.Value`, binds as a parameter and reads a stored value without validation, in the
  native binary. EF limitations met on the way, not ours to fix:
  - the compiled model cannot write the sentinel of a **struct value object wrapping a reference
    type** (`default(Code)` converts to `null`, and EF then needs a literal for the struct itself).
    Such a value object on an entity is a `sealed partial record` instead;
  - EF 10's precompiled queries cast an entity to an internal interface, which does not compile for
    a **sealed** entity class.
- **Globalization-invariant mode** ([decision 59](adr/0059-text-guard-refuses-what-it-cannot-normalize.md)).
  Native AOT templates set `InvariantGlobalization`, and there `string.Normalize` returns its input
  unchanged (measured with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, which leaves the
  `System.Globalization.Invariant` switch unset). `AsText()` asks once whether a decomposed letter
  composes, and throws `PlatformNotSupportedException` where it does not; `InvariantGlobalizationTests`
  runs the test assembly again in that mode. The smoke test's `aot` consumers do not call `AsText()`; its
  `walkthrough` consumer does, on the JIT, without that mode.
- **Third-party packages** report their own warnings (NodaTime.Serialization.SystemTextJson 1.4.0,
  EF Core). The smoke test asserts only that none names a CodoMetis package or a woven value object.
