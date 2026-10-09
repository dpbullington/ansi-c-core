# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

AnsiCCore is a **source-to-source transpiler, written in C#, that converts ANSI C (C89,
no language or compiler extensions) into clean, runnable C# source.** It is purely a
source tool — it never links against native object code and emits no IL directly; the
output is `.cs` files plus a reference to a small runtime library.

Guiding goal ("the final boss"): take an arbitrary complete C application and produce a
C# source set that compiles and runs with behavior matching the original.

Two design pillars shape nearly every decision:

- **COM-style late binding for CLR interop.** Where transpiled C needs to reach .NET
  types/APIs, it does so through a late-bound, COM-like dispatch model (instantiate by
  name, invoke methods/properties dynamically) rather than statically resolved calls.
  This keeps the emitter decoupled from the exact shape of target CLR types.
- **A safe .NET analog for C memory management.** C pointers, `malloc`/`free`, pointer
  arithmetic, and aliasing are mapped onto a *safe* managed model (no `unsafe`/raw
  pointers in output by default). The model lives in `AnsiCCore.Runtime` and the emitter
  targets it. Preserving C pointer semantics inside a safe managed representation is the
  hardest part of the project — treat the runtime memory model as a first-class design
  artifact, not an implementation detail.

## Toolchain

- **.NET 10 SDK** (`dotnet --version` → 10.x). All projects target `net10.0`.
- Solution file is **`AnsiCCore.slnx`** — the .NET 10 XML solution format, not `.sln`.
  `dotnet build`/`test` with no argument find it automatically; don't pass `AnsiCCore.sln`.
- Test framework is **NUnit**.

## Commands

```bash
dotnet build                      # build the whole solution
dotnet test                       # run all tests
dotnet run --project src/AnsiCCore.Cli -- <input.c ...> [-o <out-dir>]   # run the transpiler CLI

# run a single test class or method (NUnit via dotnet test filter)
dotnet test --filter "FullyQualifiedName~ScaffoldSmokeTests"
dotnet test --filter "Name=Harness_Runs"

# build/test one project
dotnet build src/AnsiCCore.Emit
dotnet test tests/AnsiCCore.Tests
```

Common build settings (target framework, `Nullable`, `ImplicitUsings`,
`TreatWarningsAsErrors=true`) are centralized in `Directory.Build.props` at the repo root
— change them there, not per-project. **Warnings are errors**, so keep the build clean.

## Architecture

The transpiler is a classic multi-stage compiler pipeline, one project per stage. Data
flows strictly left-to-right; project references enforce the direction (a stage may only
reference earlier stages).

```
C source ─► Frontend ─► Semantics ─► Emit ─► C# source (+ Runtime reference)
                                   ▲
                         Driver orchestrates all stages
                         Cli is a thin wrapper over Driver
```

- **`src/AnsiCCore.Frontend`** — lexer, preprocessor, and a **hand-written recursive-descent
  parser** producing a C AST. ANSI/C89 only: reject or do not special-case GNU/MSVC
  extensions. The preprocessor (`#include`, macros, conditionals) is our responsibility,
  not a third-party tool.
- **`src/AnsiCCore.Semantics`** — semantic analysis over the AST: symbol tables, scopes,
  the C type system, type checking, and resolution. Annotates/lowers the AST into a form
  the emitter can translate without re-deriving C semantics.
- **`src/AnsiCCore.Emit`** — translates the analyzed tree into C# source. Owns the two
  pillars in practice: lowering C memory operations onto the `AnsiCCore.Runtime` memory
  model, and generating the COM-style late-bound interop calls. References `Runtime` for
  the shim type *metadata/names* it emits against.
- **`src/AnsiCCore.Runtime`** — the runtime library the **emitted** C# depends on: the
  safe memory model (managed analog of the C heap/stack, pointers, pointer arithmetic),
  the late-binding/COM-style dispatch machinery, and C standard-library shims. This is the
  one project whose public API is a contract with *generated code*, so treat changes to it
  as semver-sensitive.
- **`src/AnsiCCore.Driver`** — orchestrates Frontend → Semantics → Emit for a set of
  translation units; owns multi-file / whole-program concerns.
- **`src/AnsiCCore.Cli`** — argument parsing and process entry point; delegates to Driver.
- **`tests/AnsiCCore.Tests`** — NUnit tests across all stages.

### Where things belong (avoid layering mistakes)

- C-language knowledge (grammar, types, preprocessor) stays in Frontend/Semantics. Emit
  should consume an analyzed tree, not re-parse or re-type-check.
- Anything the *generated* program needs at run time goes in `Runtime`, never in Emit.
  Emit only emits source that calls into Runtime.
- The memory-model mapping and the late-binding interop model are cross-cutting contracts
  between Emit (producer of calls) and Runtime (provider of implementations). When changing
  one side, change the other in the same step and keep a test that transpiles + runs.

## Testing approach

As the pipeline fills in, favor **golden-file / end-to-end tests**: given a `.c` input,
assert on emitted `.cs` (snapshot) and, for the "final boss" cases, that the emitted C#
actually compiles and runs with output matching a reference. The current
`ScaffoldSmokeTests` is a placeholder — replace it with real stage and end-to-end tests.

## Status

Greenfield. The solution scaffold builds, tests pass, and the CLI runs, but the pipeline
is **not yet implemented** — every stage project is currently an empty shell and the CLI
prints a "not yet implemented" stub. Build from the Frontend forward.
