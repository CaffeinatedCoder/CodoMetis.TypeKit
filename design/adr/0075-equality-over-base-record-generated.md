# 0075. Over a base record a value object's equality is generated; the base's is CMTK1023, a Warning

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an outside review of 1.0.0 and 2.0.0; [Aspects](../aspects.md).

A derived record's synthesized equality calls the base's, so the value object's equality was the
wrapped value's only where the base added nothing. Two holes, both since 1.0.0 and reproduced on
2.0.0: a base declaring `Equals(TBase)` and `GetHashCode()` by reference made two value objects holding
42 unequal while `CompareTo` answered 0, since CMTK1011 read only the value object's own members; and a
base in another assembly holding a private identity, which metadata import and a reference assembly
leave out, put two of them in a `HashSet` and one in a `SortedSet`, and a JSON round trip gave back a
value unequal to the one written. No compile-time check can see that field.

`ValueObjectContractAspect` introduces `Equals(TSelf)` and `GetHashCode()` over `_value` for every
record class with a base, which the record's synthesized `Equals(object)`, `Equals(TBase)` and `==`
call, so what a base holds or declares no longer takes part ([decision 32](0032-value-object-holds-wrapped-value-alone.md)).
Refusing every base from another assembly would have closed the second hole too, and broken a value
object over a referenced base whose sealed `ToString()` masks it, a tested pattern.

A base's hand-written equality is then not called. Replacing it without a word is this repository's
characteristic failure, and an Error that reports more waits for a major version
([decision 34](0034-errors-fixed-within-major-version-warnings.md)), so CMTK1023 names it as a Warning
and the value object is generated. It may become an Error in 3.0.0.
