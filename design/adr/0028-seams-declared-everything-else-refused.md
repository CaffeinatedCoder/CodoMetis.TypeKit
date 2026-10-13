# 0028. Seams are declared, everything else refused

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, §5.

: `TryFrom`, `FromKnownGood`,
`Revalidate`, `CompareTo(TSelf)` and `ToString()` may be written by hand and are kept; any other
member or attribute the generators introduce is CMTK1011, never kept silently. Since phase 11 so
is an explicit implementation of an interface they implement, a hand-written equality, and
`PrintMembers` without `ToString()`, declared or inherited from a base record, whose `sealed`
`ToString()` is the seam; state besides the wrapped value is CMTK1012.
