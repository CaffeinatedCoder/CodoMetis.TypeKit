# 0035. SQLite's autoincrement is the application's line, not the convention's

Date: 2026-09-28
Status: accepted

Context: 2026-09-28, §4.

EF's
SQLite provider decides by the CLR type, and the only other way in is an annotation typed by the
SQLite assembly: setting it means naming the provider or writing a foreign annotation into every
other provider's model. The README documents `UseAutoincrement()`; `MigrationSnapshotTests` fails
once EF fixes the check, which is when the note can go.
