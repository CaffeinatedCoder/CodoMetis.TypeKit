# 0072. A rule's identity is its type; its wire name stays without a namespace

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an external review of the unreleased 2.1.0; refines
[decision 67](0067-rule-that-refused-on-fault.md).

`Fault<TFault>.Rule` names a rule after its type's nesting chain and its member, with no namespace and
no type arguments, so `Shipping.Rule.Blank` and `Billing.Rule.Blank`, or `Outer<int>.Rule.Blank` and
`Outer<string>.Rule.Blank`, both read `Rule.Blank`, and two otherwise identical faults compared equal.

**Equality** compares the rule's type and member, not the string: `WithRule` keeps the rule's type in
a private member beside `Rule`, which the record's equality and hash code include, and a second
`WithRule` replaces both. Two faults whose rules come from different types are unequal however their
rules read. No public member is added.

**The wire name** stays the nesting chain and the member. A namespace would publish the application's
internal structure to every client, and a closed set's `QualifiedName`, which its overload writes, has
none either. Two rule types with the same nesting chain in one API therefore read alike to a client.
That is the application's vocabulary, as a fault's name is: it names its rule types apart, for instance
by nesting a rule in its value object (`Email.Rule.Blank`). The AspNetCore README says so.
