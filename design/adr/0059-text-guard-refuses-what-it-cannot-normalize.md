# 0059. The text guards refuse what they cannot normalize, and `AsText()` throws where nothing composes

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; refines
[decision 43](0043-exceptions-type-argument-constraints-constructor-guarantee.md), which shipped the
text guards. Measured on SDK 10.0.401, runtime 10.0.12 (Unicode 16 tables), osx-arm64 with ICU.

The guards promise that no string makes them throw and that a value is kept in one spelling. Three
inputs broke that, and each is now refused or refused to run, never answered as if nothing happened:

- **`U+FFFE`.** `string.Normalize` throws on an unpaired surrogate and on `U+FFFE`, the byte-order
  mark read in the wrong byte order, and on nothing else: a sweep of every scalar value through
  `AsText()` found `U+FFFE` alone (`U+FFFF`, `U+FDD0`, `U+1FFFE` pass). It is `Hidden` in both
  guards, as an unpaired surrogate already was, and the sweep is a test.
- **A long run of combining marks.** Composition reorders a run of marks by their classes in time
  that grows with the square of the run: a letter, n marks of one class and n of another took 0.25 s at
  n = 10,000, 4.1 s at 40,000 and 64 s at 160,000, where ten million plain characters take 0.23 s,
  and the guide runs `AsText()` before a length check. A run of more than 30 marks (Mn, Mc, Me) is
  `Hidden` in both guards, counted in the linear scan before anything is composed. Thirty is the
  Stream-Safe Text Format's bound (UAX #15). Every character of a non-zero combining class, and every
  one whose canonical decomposition starts with one, is a mark (checked against Unicode 17's data), so
  the bound bounds the reordering; counting all marks, not only those of a non-zero class, refuses a
  little more and needs no class table the runtime does not expose.
- **Globalization-invariant mode.** There `string.Normalize` returns its input unchanged, and
  `AsText()` answered `Zoe` with a combining diaeresis as a success, uncomposed, and kept `U+212B`
  as it came: one name, two spellings, silently. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` does not set
  the `System.Globalization.Invariant` switch, so `AsText()` asks once whether a decomposed letter
  composes, and throws `PlatformNotSupportedException` where it does not, on its first call whatever
  the string, its message naming the mode and the way out: the mode off and ICU available, both, since
  ICU alone does nothing while the mode is on. Not a fallback to `AsIdentifier()`'s behaviour, which
  is the silent answer itself, and not a `TextFault`, which describes a string and not the process.
  `AsIdentifier()` composes nothing and is unaffected, but is no replacement: it refuses the joiners
  written text needs and folds no space, so written text has no guard in the mode.

Two corrections to what is refused rode along. The Khmer inherent vowels (`U+17B4`, `U+17B5`) and the
Khitan filler (`U+16FE4`) render nothing and are default-ignorable, but are marks, so no category test
reached them: they are `Hidden` now. The prepended concatenation marks (`U+0600` to `U+0605`, `U+06DD`,
`U+070F`, `U+0890`, `U+0891`, `U+08E2`, `U+110BD`, `U+110CD`) are format characters by category, yet
each is drawn, spanning the digits after it, so refusing them refused Arabic and Syriac text the guard
says it keeps: they are kept, also with nothing after them, since one alone draws itself.
