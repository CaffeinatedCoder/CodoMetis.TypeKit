# 0051. A closed set parses ignoring case only when asked, and refuses members that differ only in case

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

The ordinal parse stays the default and the only one JSON, the
column, binding and `IParsable` use, since what is stored or sent is the name as it is. A header
or a query string a client typed is read with `ignoreCase: true` at the call, as
Ardalis.SmartEnum's `FromName(name, ignoreCase)` is, and not through a per-set setting, which
would change what the wire accepts without a word at the call site. With the overload, two
members whose names differ only in case are one name to the parse, so CMTK1021 refuses them at
the declaration, where it already refused the first-letter case (`Open`, `open`) that gave
`Match` one parameter twice. The peers meet such pairs at run time: Ardalis throws on the first
ignore-case lookup, Thinktecture on the first `Get`, and Intellenum answers the first declared.
