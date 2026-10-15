# 0070. A value of the package's input enums that names no member is a warning (CMTK0014)

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, an audit of every public surface that takes an enum, after
[decision 67](0067-rule-that-refused-on-fault.md) made `WithRule` refuse a value no member names.

C# lets any number be an enum. Of the package's enums, two are input: `FaultKind`, which a fault
set's member declares, and `ConstructorAccessibility`, which `[RequireParameterlessConstructor]`
takes. A member of no kind was refused only when the host mapped the set, at start; an accessibility
no member names was read by CMTK0011 as `Public`, silently, under a comment that excused it for a
base package newer than the analyzer, which cannot happen, since the base package depends on the
analyzer at its own version or later.

CMTK0014 reports a constant of either enum that names no member: a conversion, a `default`, a zero
converted implicitly, and a binary or unary expression or a constant conditional folded to one. Only
the outermost constant is judged, the value that reaches its use: an operand no member names, folded
into a member (`(FaultKind)0 + 1`), is silent. A value
only compared is a guard and stays silent: equality and relational operators, patterns and `case`.
The members are read from the enum's fields, so one a newer base package adds counts. A Warning, as a
new rule in a minor version is ([decision 34](0034-errors-fixed-within-major-version-warnings.md)).
CMTK0011 keeps reading an undefined accessibility as `Public`, the strictest requirement.

The audit's other findings needed nothing new. `FaultResponse.For` throws for a kind no member
names; a fault an application's enum does not name is refused where `ToHttpResult()` answers it and,
given to `WithFaultStatus`, when the host starts; `WithRule` throws. A value object over a plain enum
reads a number no member names, as its wrapped enum does, which [decision 26](0026-value-object-s-json-wrapped-type.md)
promises; the docs say so and show the guard.
