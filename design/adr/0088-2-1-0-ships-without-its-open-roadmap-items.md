# 0088. 2.1.0 ships without its open roadmap items, which move to 2.2

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, preparing the 2.1.0 tag; amends where [decision 52](0052-cause-on-fault-joins-2-1.md)
and [decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md) placed their work.

The roadmap's "2.1, additive" section listed work none of 2.1.0's commits took: the optional `Cause` on
`Fault<TFault>` (decision 52), the opt-in that withholds the `fault` name (decision 64), CMTK0009 on an
`IQueryable<T>` given a delegate, the generated members' XML documentation (blocked upstream), the default
`ulong` key, the CMTK0003, CMTK0010-cost and LAMA0041 items, and the run-time list. What 2.1.0 does hold, the
described converters, faults of several types, `WithoutFaults`, the type-constraint kinds and the fixes of
three audits, is complete and verified, and holding it for the rest would ship nothing sooner.

**Decision.** 2.1.0 is tagged as it stands. The section becomes "2.2, additive", unchanged in content;
decisions 52 and 64 stand as decided, with 2.2 as their release. Every item is additive, so none needs a
major version, and none was announced to consumers as 2.1.0's.
