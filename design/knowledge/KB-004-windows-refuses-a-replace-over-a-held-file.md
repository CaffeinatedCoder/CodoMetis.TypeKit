---
verified: 2026-10-15
pins:
  - sdk 10.0.4xx
source: File.Move over a file another FileStream holds open with FileShare.ReadWrite and FileShare.Delete, on GitHub's windows-latest runner (runs 38732628888 and 38734437033 failed on it, 38736660731 passed with the file moved aside) and on macOS
turned-on: decision 92 (how every tool replaces a file) and the fix 617db19 (tools/Shared/SharedFile.cs)
test: test/CodoMetis.TypeKit.Facts/KB004.cs
---

# KB-004. On Windows, a rename that replaces a file someone holds open is refused, whatever its share mode, but moving the held file aside is allowed

`File.Move(temporary, target, overwrite: true)` while another handle has `target` open:

- **Windows** refuses it with `UnauthorizedAccessException` ("Access to the path is denied"), even when that
  handle allows `FileShare.Delete`. Moving `target` itself aside (`File.Move(target, aside)`) is allowed, and
  then the move into its place succeeds.
- **Linux and macOS** replace it; the open handle keeps reading the old content.

So a reader that allows `FileShare.Delete` is necessary on Windows and not sufficient: a writer that replaces
the file must fall back to moving the old one aside, as `SharedFile.Replace` does
([decision 92](../adr/0092-repository-tooling-is-dotnet-and-ci-needs-no-ai.md)).
