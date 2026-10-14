# 0065. 2.0.0 ships without a preview

Date: 2026-10-14
Status: accepted

Context: 2026-10-14, preparing the 2.0.0 release; [decision 44](0044-2-0-0-net10-0-metalama.md).

Decision 44 turned the `2.0.0-preview.N` branch into a preview of 2.0.0 itself from `main`, and the
roadmap held `2.0.0-preview.1` on nuget.org, then the consumer smoke test against it, as a step
before the tag. It had been on hold since 2026-10-13. The user dropped it: 2.0.0 is tagged without a
preview, and no `2.0.0-preview.N` is published.

What the preview would have measured, the packed packages installed into throwaway consumers on
Native AOT among them, the consumer smoke test already measures against the packed feed, in CI on
every pull request to `main` and again in the release workflow before the approval gate. The rest of
decision 44 stands.
