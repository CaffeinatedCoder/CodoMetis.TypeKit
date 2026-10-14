# 0053. A `Fault<TFault>` is made by `Fault.Of` alone

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; [Option and Result](../option-result.md).

`Fault<TFault>` had an implicit conversion from its reason, and the first review's fix refused
`fault == reason`, `!=` and `Equals(TFault)`. The conversion still reached a comparison everywhere
else: `Fault<F>? f == F.NotFound`, `List<Fault<F>>.Contains(F.NotFound)`, a `HashSet`,
`EqualityComparer<Fault<F>>.Default.Equals(f, F.NotFound)`, and Testing's
`ShouldBeError(F.NotFound)`, each compiling and false whenever the fault carried a detail (measured).
The conversion is gone; `Fault.Of(reason)` and `Fault.Of(reason, detail)` make a fault. The refused
operators and `Equals(TFault)` stay, since without them `==` is CS0019, which does not say to
compare `Reason`, and `fault.Equals(reason)` binds to `Equals(object)` and is always false.
`Array.IndexOf(array, reason)` still compiles, through `Array.IndexOf(Array, object)`, as
`object.Equals` does; nothing in the type can refuse it. `Fault<TFault>` is new in 2.0.0, so this
breaks nothing that shipped.
