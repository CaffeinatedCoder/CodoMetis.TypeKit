# 0041. A sensitive value object is one that declares `ToString()`; Serilog gets a satellite

Date: 2026-10-11
Status: accepted

Context: 2026-10-11,
phase 12, spikes/SensitiveValues.

The seam already masks every formatting and
Microsoft.Extensions.Logging path, so a marker would generate the one line a user writes, and is
not added. Serilog's `{@x}` read the public `Value` of every value object, masked or not:
`CodoMetis.TypeKit.Serilog` registers one destructuring policy, `Destructure.WithTypeKit()`, that
logs a value object as a scalar of its `ToString()`, recognised by `GeneratedValueObjectAttribute`.
Every value object is then a string in the event, as Microsoft.Extensions.Logging writes it, rather
than its wrapped value's JSON type: what it prints is the one rule, and the policy cannot tell a
masked `ToString()` from the generated one. Serilog's floor is 4.0.0, the first of the current
major, and the tests run on it. A classification on the type is advised against: the logging
generator refuses the method (LOGGEN035).

Still open:
