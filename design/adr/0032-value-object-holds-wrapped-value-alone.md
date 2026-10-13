# 0032. A value object holds its wrapped value alone, and its equality is that value's

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, [Aspects](../aspects.md).

Everything generated (JSON, parsing, the type converter, the EF column, comparison) carries
the wrapped value, so other state (CMTK1012), a hand-written equality or an explicit
implementation beside a generated member (CMTK1011, CMTK1008) would make two views of one value
disagree. Normalise in `Create` instead.
