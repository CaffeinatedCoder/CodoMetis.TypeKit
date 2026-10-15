---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: System.Text.Json in the .NET 10 runtime, measured with a resolver inserted first in TypeInfoResolverChain and a conflicting converter in JsonSerializerOptions.Converters
turned-on: decision 83 (a description asks the contract's converter first)
test: test/CodoMetis.TypeKit.Facts/KB007.cs
---

# KB-007. A resolver of one's own decides a type's contract alone, over a converter in the options, and `T?` follows `T`'s contract

With `DecimalStringConverter` in `JsonSerializerOptions.Converters` and nothing else, a `decimal` and a `decimal?`
are written `"1.2"`. Insert a resolver ahead of the default one in `TypeInfoResolverChain` that answers for
`decimal` with a contract over a number converter, and both are written `1.2`:

- the options' converters are consulted by the reflection resolver when it builds a contract; a resolver that
  builds its own contract is not bound by them;
- the reflection resolver builds `Nullable<decimal>`'s contract from the contract the chain gives `decimal`, so
  the nullable position follows the resolver too.

So what the serializer writes for a type is its contract's converter (`options.GetTypeInfo(type).Converter`), not
the first matching converter in the options ([decision 83](../adr/0083-the-contract-s-converter-first-and-enum-proves-nothing-against-struct.md)).
