# 0068. Fault sets that share members are spiked before they are built

Date: 2026-10-14
Status: superseded by 0078

Context: 2026-10-14, a review of an application moving its own fault responses onto the package;
[decision 49](0049-fault-responses-client-can-act.md), [decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md).

An application whose boundaries each declare a fault set answers some refusals the same way in all
of them: a change that raced the request, a stale version, an operation that did not complete. A
member belongs to one set, so each boundary declares its own copy, and a shared translation that
decides those refusals once has to return a set of its own that every boundary then maps member by
member. The review asked for composition: a set that includes another set's members.

Two shapes answer it, and neither is measured: a set that declares the sets it includes, whose
members the generators copy, by name, kind and description, with a conversion from the included set;
and C# 15 unions, where a boundary's error is a union of its own set and the shared one, which
3.0.0 takes up ([decision 44](0044-2-0-0-net10-0-metalama.md)). Both decide what the problem's `fault`
says, what the document lists and what `AddFaultResponses` maps, so the choice is a spike's, listed
under "2.x, with a spike first". Until then a boundary declares the shared members it answers with,
and maps the shared set onto them at the boundary, as decision 64 maps two causes onto one.
