---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: System.ComponentModel.TypeDescriptor in the .NET 10 runtime, measured with a type carrying a TypeConverterAttribute
turned-on: the fix 1ee02fc (a value object over a NodaTime type parses after MVC described the type), GeneratedParsing's fallback and design/aspects.md
test: test/CodoMetis.TypeKit.Facts/KB003.cs
---

# KB-003. A type `TypeDescriptor.GetConverter` described first can no longer be registered

`TypeDescriptor.RegisterType<T>()` followed by `TypeDescriptor.GetConverterFromRegisteredType(typeof(T))` is the
trim-safe way to the converter a type's `[TypeConverter]` names. Once anything in the process has called
`TypeDescriptor.GetConverter(typeof(T))`, as MVC does binding a plain `LocalDate`:

- `RegisterType<T>()` does nothing: `TypeDescriptor.GetProvider(typeof(T)).IsRegisteredType(typeof(T))` stays false;
- `GetConverterFromRegisteredType(typeof(T))` throws `InvalidOperationException` ("The type … is not registered"),
  for the rest of the process;
- `GetConverter(typeof(T))` still answers, with the attribute's converter.

A type registered before anything described it is registered, and the registered lookup returns that converter.
The order is decided by whoever touches the type first, which a library cannot control
([Aspects](../aspects.md)).
