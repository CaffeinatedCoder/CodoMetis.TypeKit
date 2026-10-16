# 0102. An exclusion carries its reason, claims.json is held to its schema, and the prober writes JSON with only JSON's own escapes

Date: 2026-10-16
Status: accepted

Context: 2026-10-16, after the probe gate round for 2.2.0 (pull request #20), whose four agents wrote 18
probes. A claim in `tools/probe/corpus/claims.json` drops an axis by quoting the sentence that limits it
(`narrow`, checked against the document), and drops pairs no row can hold by listing them (`exclude`, checked
by nothing). The schema, `tools/Prober/Schemas/claims.schema.json`, had no field for why a pair is excluded,
and the prober read claims.json without checking it against the schema at all. Three agents each invented a
field of their own for the reason: `excludeReasons` as a list of `{match, reason}`, `excludeReasons` as an
object keyed by its match, and `excludeWhy` as a list of `{pairs, reason}`, each naming its pairs in prose.
Nothing read any of them, so a reviewer reading `prober universe` saw how many pairs a claim had, never what
it left out or why. And `prober publish` rewrote claims.json through `Files.WriteJson`, whose default encoder
writes `+`, a backtick, `<`, `>` and `'` as six-character escapes: the 1,574 pairs and the reasons are full of
them (`Option<T>`, `1E+2`, code in backticks), so a publish turned every such character into an escape and
every agent edited the file by hand instead.

## Decision

**An exclusion is a reason and its pairs.** Each entry of a claim's `exclude` is an object:

```json
{ "reason": "A closed set wraps nothing (Generators README#closed-sets), so it has no shape.wrapped.",
  "pairs": [["shape.kind=closed-set", "shape.wrapped=Guid"], ["shape.kind=closed-set", "shape.wrapped=int"]] }
```

`reason` is one sentence saying why the pairs cannot exist or are not promised, citing the docs sentence, the
record or the KB fact where one applies; it is required and may not be blank. `pairs` lists the pairs, at least
one, each two `axis=value` settings, as before. `narrow` is unchanged. A pair's effect is what it was: the
covering array never asks for it and the gate never counts it.

**The prober holds claims.json to its schema.** The schema says `additionalProperties: false` for a claim and
for an exclusion, so `excludeReasons`, `excludeWhy` and any other field nobody reads are refused. The prober
checks every claims file where it reads one (the corpus's and the branch's staging, for `universe`, the gate,
and both sides of `publish`) and refuses with each problem named by its claim and field
(`claim C20-testing-shouldhold: exclude[0].reason is required`). The check (`SchemaCheck`) reads the schema
embedded in the dll and knows the keywords the schema uses; a keyword it does not know is an error, never a
pass.

**`prober universe` shows what nothing checks mechanically.** Under each claim's line it prints the axes the
claim narrows, with their quotes, and each exclusion with its pair count, its first pair and its reason.
`universe.json` and `status --json` do not change: they carry the pairs, not the reasons.

**The prober writes JSON with only JSON's own escapes.** `Files.WriteJson` uses
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` and a `\n` line end on every OS. `+`, backticks, `<`, `>` and `'`
stay literal, and a tracked file in the form `publish` writes is unchanged by a publish that changes nothing in
it. The relaxed encoder is unsafe only for JSON embedded in HTML or a script, which none of these files is; no
reader parses them as anything but JSON, and a probe's identity hashes the canonical form, not the file's text,
so no identity changes.

## Consequences

- An agent writes an exclusion with its reason, in the one field, and `prober universe` is where a reviewer
  reads it. A reason the round invents somewhere else fails the next `universe`, `gate` or `publish`.
- The migration is done: a one-off file-based app, run once and deleted, put each of the 51 invented reasons
  over the pairs its prose named and wrote the two reasons C20 lacked. claims.json is now in the form `publish`
  writes, 7,612 lines (3,771 before: most pairs had been folded onto one line by hand), and a test in the
  prober tests holds both the schema and the form for the tracked file.
- A schema the prober reads at load can no longer say more than is checked: a new keyword in it needs
  `SchemaCheck` to learn it first. Only claims.json is checked this way today; the other schemas stay
  documentation until a file of theirs is found carrying a field nobody reads.

## Evidence

Measured 2026-10-16 on macOS (arm64) with the pinned SDK, on `claude/loving-napier-535af5` (pull request #22's
branch with release/2.2.0 merged in, d8b7cd7):

| | Count |
|---|---|
| Claims | 42 |
| Claims with `narrow` | 22 |
| Claims with `exclude` | 19 |
| Excluded pairs | 1,574 |
| `excludeReasons` as a list of `{match, reason}` | 4 claims, 18 reasons |
| `excludeReasons` as an object keyed by its match | 9 claims, 15 reasons |
| `excludeWhy` as a list of `{pairs, reason}` | 5 claims, 18 reasons |
| Claims with `exclude` and no reason anywhere | 1 (C20, 3 pairs) |
| Exclusions after the migration | 53 (51 migrated, 2 written for C20) |
| Escapes in claims.json before (`'` 25, `+` 3, `<` 14, `>` 21, backtick 92) | 155, on 38 lines; 0 after |

- Every excluded pair of every claim is in exactly one exclusion after the migration, and the set of pairs per
  claim is the one before (compared per claim, sorted). Everything else in every claim is unchanged.
- `prober universe --base origin/main` printed the same line per claim, `universe: 42 claims, 227285 pairs`,
  from the tool before this change over the old claims.json and from the tool after it over the new one, and
  the two `universe.json` files hold the same axes and the same excluded pairs per claim.
- A `publish` of a staging holding the migrated claims.json unchanged left the corpus's claims.json byte for
  byte as it was.
- The new tests in `ClaimsFileTests` were each shown to fail with their defect seeded: `reason` not required,
  a blank reason allowed, a claim's or an exclusion's fields left open, the check's result ignored, the
  default encoder, the exclusion or the narrowing line left out of `universe`, and an unknown schema keyword
  passed over.
