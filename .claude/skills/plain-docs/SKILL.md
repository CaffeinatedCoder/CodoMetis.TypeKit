---
name: plain-docs
description: The voice for every page a consumer reads in CodoMetis.TypeKit: the root README, the package READMEs and the guides in docs/. Plain, friendly, example-first and exact. Use when writing or rewriting any of them, including a new section, a new docs page, or a paragraph added beside a feature.
---

# Plain docs

The reader is a .NET developer deciding whether to try TypeKit, or trying it right now. They should
understand each paragraph on the first read, and finish a section knowing what to type. A page that is
correct but hard to read loses them before the details matter.

This voice is friendly, not chatty. Every claim stays as exact as before. Plain words make the docs
readable; they never make them vaguer.

## The voice

- **Write to "you".** "You write `Create` once" rather than "`Create` is the one method written by hand".
- **Say what the reader gets first, then how it works.** "Your addresses are trimmed and lower-cased
  in one place" comes before "every generated entry point calls `Create`".
- **One idea per sentence.** Split a sentence at a semicolon, a colon followed by a list, or a second
  "and". About 20 words is a good ceiling.
- **Active voice, a person or a thing doing something.** "The build fails and names the type" rather
  than "a build error naming the declaration".
- **Natural word order.** Not "What X is, Y does", not "Every way in applies the one `Create`".
- **Don't personify the code.** A fault doesn't "reach" a response, a document doesn't "disagree",
  a type doesn't "say". Write what happens: "the error ends up in the response".
- **State things positively when you can.** "Use `TryFrom` when you only need yes or no" instead of a
  chain of "never", "not" and "nothing".
- **No hype and no filler.** Avoid "simply", "just", "easy", "seamless", "powerful", "blazing", exclamation
  marks and emoji. Friendly comes from clarity, not from adjectives.

## Plain words

Use the left column in prose. Keep the API's own names in code (`EmailFault`, `Fault<T>`,
`AddFaultResponses`), and introduce a term once where the API forces it on the reader.

| Write | Instead of |
|---|---|
| error, or "the error type (TypeKit calls it a fault)" on first use | fault, as a bare concept |
| underlying type, the value inside | wrapped type |
| reject, fail | refuse, refusal |
| the EF Core package, the ASP.NET Core package | satellite |
| read back without validation | materialize, materializer |
| generated | woven, introduced |
| every way a value gets created: JSON, parsing, `TryFrom`… | every entry point, every way in |
| the build fails (CMTK1012) | is CMTK1012 |
| your code, your project | the source, the consumer, the caller |
| a rule, a guarantee | a contract, what it holds to |

Name a diagnostic after the plain description, in parentheses: "a second field fails the build
(CMTK1012)". The id helps someone searching; the words help everyone else.

## Page shape

1. **Open with one or two sentences** on what the page lets the reader do. Then show the smallest
   example that works.
2. **Headings are tasks or questions** in guides: "Validate input", "Pick the right factory",
   "Use value objects in generic code". A package README may keep reference headings (`## CMTK0001`)
   where code links to them.
3. **Example first, explanation after.** Each section leads with code where there is code, then says
   what it does in a few short sentences.
4. **Edge cases go last**, under a short "Good to know" list at the end of the section, not in the
   middle of the explanation. Each one is a bullet with a bold lead phrase.
5. **Lists for three or more parallel items.** A bullet starts with a bold phrase that a skimmer can
   read alone, followed by one or two sentences.
6. **Link instead of repeating.** A guide explains and a package README is the reference; the same
   detail lives in one place and the other links to it.

## What must not change

Rewriting the voice must not break what the tests guard. Run the conventions and samples tests after
every page.

- **Code blocks are compiled.** A C# block in a README or docs page is a region in
  `test/CodoMetis.TypeKit.Samples`. Leave the code as it is. A comment that states an output (a value,
  JSON, SQL) is read by a test, so its value stays exact. You may reword a comment's prose part, but
  then run the samples tests.
- **Anchors are checked.** Renaming a heading changes its anchor. Find every link to it
  (`git grep -n 'page.md#anchor'`) and update them in the same change. Keep the headings of package
  READMEs that exception messages and analyzer help links point at (`#cmtk0001`, `#build-errors`, and
  the like). Check with `git grep -n 'README.md#' -- src` before renaming one.
- **Facts stay exact.** If the old sentence had a condition, an exception or a version, the new one
  has it too, perhaps moved into "Good to know". When unsure what a sentence means, read the code or
  the test it describes before rewriting it.
- **The Documentation table** in the root README links every docs page. Keep its rows in step with
  the pages' new opening sentences.
