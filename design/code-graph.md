# The code graph

The codebase-memory MCP server keeps a graph of this repository, the project `CodoMetis.TypeKit`.
[AGENTS.md](../AGENTS.md), "Finding code", says how to use it: the graph leads and the source decides.
This note holds what that rests on: what the graph was measured to find and miss.

## What it finds and misses

Measured on 2026-10-13, the graph:

- finds types, interfaces, enums and ordinary methods, a type's implementers through generic interfaces
  (84 of the Samples' 88 value objects), and the callers of an ordinary method across files;
- has no node for a member of a C# 14 `extension(...)` block, which holds most of the `Option` and `Result`
  extensions (`Choose`, `FirstOrNone`, `Transpose`, `Sequence`, the continuations of a `Task<Result<...>>`)
  and `AsText`, and puts the calls inside one on the file;
- has nothing Metalama generates: none of the tests' 222 `.From(` calls has an edge, nor has a call to
  `TryFrom`, `FromKnownGood`, `Parse` or a converter, and a template an aspect introduces by `nameof` looks
  unreferenced. Read woven code as the Generators README's "Reading the generated code" says;
- misses a reference that is not a call: a DI registration, a type argument, `typeof`, an attribute;
- resolves a call on a generic instance poorly (`Option<T>.Match` has one resolved caller, where the
  repository writes `.Match(` about 200 times across its types) and guesses wrong callers: a low-confidence
  `suffix_match` edge, or a same-named method of another API (Metalama's `Diagnostics.Report` listed as
  calling `GeneratedCode.Report`). Overloads share one node.

## Keeping it current

The graph does not follow the working tree. `index_repository` with the repository root as `repo_path` and
`name: "CodoMetis.TypeKit"` refreshes it in about 6 s; without `name` it starts a second project under a
derived name. `index_status` reports the checkout's HEAD, not the commit the graph was built from, so
refresh rather than check.

It indexes `spikes/` and leaves out `.claude/`, `bin` and `obj`. So an agent in a worktree under
`.claude/worktrees/` reads the main checkout's graph, not its own: it indexes the worktree under a name of
its own and deletes that project when done, or searches the source. `.codebase-memory/`, the tool's local
artifact, is ignored by git.
