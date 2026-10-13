# 0004. Metalama 2026.1.x (latest stable)

Date: 2026-09-26
Status: accepted

Context: 2026-09-26.

, not the 2027.0 preview.
   - Aspect state keeps `IDurableRef`, which exists in 2026.1.
   - `[Durable]` on the `_value` template placeholder is omitted, because it is 2027.0-only, and
 is added back on upgrade.
   - A 2026.1-built package works in a 2027.0 consumer (fabric spike finding 11).
   - Once 2027.0 is stable, upgrading is a planned task: MIT maintenance covers only the latest
 `YYYY.N`.
   - **Metalama is the generator technology for this project**, not a candidate for replacement.
