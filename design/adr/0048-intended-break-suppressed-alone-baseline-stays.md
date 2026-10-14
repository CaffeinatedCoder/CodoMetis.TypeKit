# 0048. An intended break is suppressed alone; the baseline stays

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

The plan
left the choice to the first break package validation reported, and it came with the fault
responses: `FaultProblem<TFault>` takes `notnull` where it took `struct, Enum` (CP0021), so that a
closed set can be a fault type. Each intended break gets its own entry in the project's
`CompatibilitySuppressions.xml` (generated with `-p:ApiCompatGenerateSuppressionFile=true`, then
read before it is committed), and the 1.5.1 baseline keeps comparing every other member, so an
unintended break still fails the pack. Letting the baseline lapse for 2.0.0 would have passed one.
