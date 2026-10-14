# 0067. A fault names the rule that refused, as a member of a fixed set

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, a review of an application moving its own fault responses onto the package;
[decision 49](0049-fault-responses-client-can-act.md), [decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md).

A boundary's fault says what the client can act on, `AccountFault.EmailTaken`, and a status follows
from it. Which rule refused underneath, the address in use or reserved, a value object's `Blank` or
`NoAt`, was either a member per rule in the boundary's own type, multiplying its members by every
field's rules, or a sentence in `Detail`, which a client cannot branch on. An application had built a
second key beside its code for exactly this, and learned on the way that the key must be qualified:
three value objects' `Blank` were three rules.

`Fault<TFault>` gains `Rule`, set by `WithRule(rule)` from an enum's member (`struct, Enum`) or a closed
set's (`IClosedSet<TRule>`), two extension classes since C# tells the overloads apart by constraint
alone. It holds the qualified name, `EmailRule.Reserved`, a nested type after its nesting chain as a
closed set's `QualifiedName` is, whatever `QualifiedFaultNames` says. A value no member names, a
combination of flags or a cast number, throws `ArgumentOutOfRangeException` where it is given. It
takes part in equality, as the detail does. `ToHttpResult()` writes it as the problem's `rule`,
`IFaultFeature` carries it, with a default implementation so a host's own still builds, and the
document describes `rule` beside `fault` as an optional string. The status, the mapping and the
document's responses stay keyed on the reason.

Rejected: a free string key, which a client branches on as surely and which could quote the request;
a second type argument, `Fault<TFault, TRule>`, which changes the endpoint's type with every field a
boundary validates; and the feature alone, which leaves every host to write the same extension. The
rule reaches the client as the fault's name does ([decision 64](0064-fault-name-reaches-client-causes-not-told-apart-one-fault.md)):
a boundary that must not tell rules apart names none.
