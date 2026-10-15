# 0077. A base's IEquatable is implemented again, the resolver's converter is described, and the reference scan reads what the serializer writes

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a third external review of the unreleased 2.1.0 at 543e382, five cases reproduced
before they were fixed; refines [decision 74](0074-descriptions-resolve-the-converter-as-the-serializer-does.md),
[decision 75](0075-equality-over-base-record-generated.md) and
[decision 76](0076-preserved-references-refused.md). Measured with System.Text.Json 10 and Metalama 2026.1.30.

**A base's `IEquatable<TBase>` is implemented again by the value object.** Decision 75 generated
`Equals(TSelf)` and `GetHashCode()`, which the record's `Equals(object)` and `Equals(TBase)` call, but a
collection of the base calls the base's `IEquatable<TBase>`, and a base implementing it explicitly kept
that implementation: a `HashSet` of the value object held one of two holding 42, a `HashSet` of the base
both. C# implements an interface again only where a class lists it. Metalama implements no interface a
base already does (`Override` is taken as `Ignore`, `New` is LAMA0516) and introduces no explicit member
of an interface it skipped, so the value object lists `IReimplementedEquatable<TBase>`, an empty public
interface in `CompilerServices` derived from `IEquatable<TBase>`, for every base; listing a derived
interface implements its bases again, here by the public `Equals(TBase)` the compiler synthesizes in the
record derived from that base, which calls `Equals(object)`. A base between the value object and a deeper
one that itself implements the deeper one's `IEquatable` explicitly keeps it, and CMTK1023 names it.
Rejected: CMTK1023 as an Error, which waits for a major version (decision 34); a public `new
Equals(TBase)` per base, which adds members a value object does not declare.

**The resolver's converter is described.** Decision 74 asked the resolver last, after the generated
description, so a resolver of one's own that put a described converter in charge of a value object, which
wrote `"780"`, left the exporter and the document describing the wrapped decimal. `JsonSchemaDescription.For`
now asks the converter the resolver gives, and recognises the generated one by the type's own
`[JsonConverter]` as before; the generated description is the answer only where the resolver does not
know the type, a source-generated context that does not list it.

**The reference scan reads what the serializer writes.** Decision 76 walked the wrapped type's contract
for anything that could have an `$id`. A nullable struct's contract lists none of the held struct's
properties, so a class inside one went unseen and two value objects around it wrote its `$id` twice; it
is now read as the struct it holds, unless the options convert the nullable type themselves. An ignored
property has neither getter nor setter in the contract and is neither written nor read, so a class there
refused a struct the serializer writes without one, a payload that worked before decision 76; it is now
passed over.

An abstract class's public string constructor was taken for its parser, and the generated parsing failed
(LAMA0611, CS0144); constructor parsing now takes only a type that is not abstract.
