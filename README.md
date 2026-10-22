# AnsiCCore

**A source-to-source transpiler, written in C#, that converts ANSI C (C89 — no language or
compiler extensions) into clean, runnable C# source.**

AnsiCCore is a *pure source tool*. It never links against native object code and emits no IL
directly: the output is `.cs` files plus a reference to a small runtime library. The guiding goal
("the final boss") is to take an arbitrary complete C application and produce a C# source set that
compiles and runs with behavior matching the original.

## The central design pillar

**A safe .NET analog for C memory management.** C pointers, `malloc`/`free`, pointer arithmetic,
aliasing, unions, and type punning are mapped onto a *safe* managed model — no `unsafe` code or raw
pointers in the output. The runtime models a **flat emulated address space**: `CMemory` holds byte
regions at numeric base addresses, and a `CPtr` is just a `ulong` address (0 = null) with
bounds/use-after-free checks. This faithfully supports the hard cases — stored pointers, unions,
`int`↔pointer casts, and type punning — that a naive "pointers become C# references" scheme cannot.

## Status

Working end-to-end for a large C89 subset: the full pipeline lexes, preprocesses, parses, analyzes,
and emits C# that compiles and runs. **466 tests pass** (unit + golden end-to-end), including 20
whole-program compile-and-run golden programs. The bundled C89 standard library is essentially
complete (all standard headers present).

The lexer, preprocessor, parser, and semantic type system are **near-complete for C89**. The
remaining frontier is a short, well-marked list in the emitter plus continued breadth in the
bundled C standard library — see [Coverage](#coverage).

## Toolchain

- **.NET 10 SDK** (`dotnet --version` → 10.x). All projects target `net10.0`.
- Solution file is **`AnsiCCore.slnx`** (the .NET 10 XML solution format). `dotnet build`/`test`
  with no argument find it automatically.
- Test framework is **NUnit**.
- Common build settings live in `Directory.Build.props`. **Warnings are errors** — the build stays
  clean.

## Commands

```bash
dotnet build                      # build the whole solution
dotnet test                       # run all tests
# run the transpiler CLI
dotnet run --project src/AnsiCCore.Cli -- <input.c ...> [-o <out-dir>] [-I <include-dir>] [-D name[=value]] [--link <assembly>]

# run a single test class or method
dotnet test --filter "FullyQualifiedName~EmitEndToEndTests"
dotnet test --filter "Name=Exit_TerminatesWithStatus"

# regenerate golden snapshots after an intentional emitter change
UPDATE_GOLDENS=1 dotnet test --filter "FullyQualifiedName~GoldenProgramTests"
```

## Architecture

A classic multi-stage compiler pipeline, one project per stage. Data flows strictly left-to-right;
project references enforce the direction.

```
C source ─► Frontend ─► Semantics ─► Emit ─► C# source (+ Runtime reference)
                                   ▲
                         Driver orchestrates all stages
                         Cli is a thin wrapper over Driver
```

| Project | Responsibility |
|---|---|
| **`src/AnsiCCore.Frontend`** | Lexer, preprocessor (`#include`, macros, conditionals — our own, not a third-party tool), and a hand-written recursive-descent parser producing a C AST. ANSI/C89 only. |
| **`src/AnsiCCore.Semantics`** | Semantic analysis: symbol tables, scopes, the C type system, type checking, and resolution. Annotates/lowers the AST so the emitter need not re-derive C semantics. |
| **`src/AnsiCCore.Emit`** | Translates the analyzed tree into C# source, lowering C memory operations onto the runtime memory model. Resolves external calls against an `ExportRegistry` (static `[CExport]` linking). |
| **`src/AnsiCCore.Runtime`** | The library the *emitted* code depends on: the safe memory model and the C standard-library shims (`CStdLib`, `CMath`, `CTime`, `CProcess`, …). Its public API is a contract with generated code. |
| **`src/AnsiCCore.Driver`** | Orchestrates Frontend → Semantics → Emit for a set of translation units; owns multi-file / whole-program concerns. |
| **`src/AnsiCCore.Cli`** | Argument parsing and process entry point; delegates to Driver. |
| **`tests/AnsiCCore.Tests`** | NUnit tests across all stages, plus golden programs in `Programs/` and stub headers in `Include/`. |

### Interop: static link-time binding

The supported way to reach .NET code is **static linking**, the managed analog of a link-time
object. A precompiled static method marked `[CExport("name")]` implements a C function; Emit
resolves a call to a declared-but-undefined C function (an `extern` prototype) directly to that
method via an `ExportRegistry` reflected from the linked assemblies. The C standard-library shims
are simply the default linked library, so libc and user libraries share one mechanism. User
libraries are added with `--link <assembly>`.

> The COM-style **dynamic / late-bound** dispatch model (instantiate by name, invoke members
> dynamically) is **deferred** and intentionally not built. Static `[CExport]` linking is the
> intended extension point.

Argument marshalling covers scalars, pointers (`CPtr`), strings (`char*`↔managed `string`), `bool`,
variadics (`params object[]`), and **function pointers** passed as `System.Action`/`System.Func`
callbacks.

## Coverage

### C89 language

| Area | Status |
|---|---|
| Lexer (all tokens, int/hex/octal/float/char/string literals, wide prefixes, escapes) | ✅ Complete |
| Preprocessor (`#include`, object/function macros, `##`/`#`, full conditionals, `defined`, predefined `__FILE__`/`__LINE__`/`__DATE__`/`__TIME__`/`__STDC__`) | ✅ Complete |
| Parser (declarators, initializers, all statements, full expression grammar) | ✅ Complete for the C89 grammar |
| Type system (all C89 types + qualifiers, bit-fields, promotions, usual arithmetic conversions, compatibility, integer constant expressions) | ✅ Complete |
| Emit: scalars, pointers + pointer arithmetic, all operators, control flow incl. `switch`/`goto` fall-through | ✅ |
| Emit: structs/unions by value, bit-fields, arrays, string literals, globals/statics, aggregate initializers | ✅ |
| Emit: `(void)expr` discard, compound assignment incl. `<<=`/`>>=`, value-context `++`/`--` on memory objects and bit-fields | ✅ |
| Emit: user-defined variadic functions (`<stdarg.h>`) | ✅ |
| Emit: `int main(void)` **and** `int main(int argc, char **argv)` | ✅ |
| `long long` | ⛔ Rejected (correctly — not in C89) |
| Variadic macros | ⛔ Rejected (correctly — C99) |
| **Function pointers stored in memory** (fn-pointer struct fields / address-taken fn-pointer variables) | ❌ Not yet (work as plain locals/values) |
| **K&R function *definitions*** with parameters | ❌ Parsed, rejected at emit (prototypes are fine) |
| Trigraphs | ❌ Not translated |
| `long double` precision | ⚠️ Modeled, but collapses to `double` at emit |
| `#line` line remapping | ⚠️ Parsed, not applied |

### C standard library (bundled shims)

Stub headers live in `tests/AnsiCCore.Tests/Include/`; implementations are `[CExport]` methods in
`src/AnsiCCore.Runtime/Library/`.

| Header | Coverage |
|---|---|
| `<math.h>` | ✅ Complete — all 22 C89 functions (incl. `frexp`/`modf`), plus C99 extras |
| `<ctype.h>` | ✅ Complete — all 13 classifiers/converters |
| `<string.h>` | ✅ Complete — all 22 C89 functions |
| `<stddef.h>` | ✅ Complete — `size_t`, `NULL`, `ptrdiff_t`, `wchar_t`, `offsetof` |
| `<limits.h>` | ✅ Complete — macro header |
| `<float.h>` | ✅ Complete — macro header |
| `<stdarg.h>` | ✅ Functional — `va_list`/`va_start`/`va_arg`/`va_end` for user variadic functions |
| `<stdlib.h>` | ✅ Complete — heap, `abs`/`labs`/`div`/`ldiv`, the `strto*`/`ato*` family, `rand`/`srand`, `exit`/`abort`/`atexit`, `getenv`/`system`, `qsort`/`bsearch`, the multibyte functions |
| `<time.h>` | ✅ Complete — `time`/`clock`/`difftime` plus calendar (`gmtime`/`localtime`/`mktime`/`asctime`/`ctime`/`strftime`, `struct tm`) |
| `<errno.h>` | ✅ `errno` + `EDOM`/`ERANGE`, set by `strto*` and the math functions |
| `<locale.h>` | ✅ C-locale stub — `setlocale`/`localeconv` |
| `<assert.h>` | ✅ `assert` (honors `NDEBUG`, reports the call site) |
| `<signal.h>` | ✅ `signal`/`raise` with synchronous delivery (`SIG_DFL`/`SIG_IGN`/`SIG_ERR`) |
| `<setjmp.h>` | ✅ `setjmp`/`longjmp` — see note below |
| `<stdio.h>` | ~40/41 — the `printf`/`scanf`/`v*printf` families (incl. `sscanf`), file access incl. `freopen`/`tmpfile`/`tmpnam`, buffering no-ops (`fflush`/`setvbuf`), char I/O (`getc`/`putc`/`ungetc`), positioning (`fseek`/`ftell`/`fgetpos`/`fsetpos`/`rewind`), state (`feof`/`ferror`/`clearerr`), `remove`/`rename`/`perror`. Omitted: `gets` (unsafe); binary mode (`"rb"`) is ignored |

**printf conversions:** `%d %i %u %o %x %X %p %c %s %f %e %E %g %G %%`, with flags `- + space 0`,
field width and precision (literal or `*`), and tolerated length modifiers.

**All C89 standard headers are present.** `setjmp`/`longjmp` work by an emitter transformation:
a `longjmp` throws across the stack to a loop+try/catch the emitter wraps around the block from
the `setjmp` onward. `setjmp` is therefore supported where it controls flow — as an `if`/`switch`
controlling expression or the initializer/right-hand side of a declaration/assignment (the
canonical idioms); used elsewhere it is a clear emit error.

## Testing approach

Favors **golden-file / end-to-end tests**: given a `.c` input, assert on the emitted `.cs`
(snapshot, `*.c.cs`) and — for the "final boss" cases — that the emitted C# actually compiles (via
Roslyn, in memory) and runs with output/exit code matching a reference (`*.c.out`). Snapshots are
regenerated with `UPDATE_GOLDENS=1`. Stage-level unit tests cover the lexer, preprocessor, parser,
type system, and the runtime memory model.

## Roadmap

The C89 standard library is essentially complete; the remaining gaps are language-level emit
constructs:

- **Function pointers stored in memory** (fn-pointer struct fields / address-taken fn-pointer
  variables) — the last notable language-level emit gap. (Passing a function *by name* as a call
  argument already works — `qsort`/`atexit`/`signal` callbacks are fine — and a null function
  pointer now casts correctly.)
- **K&R function bodies** at emit.
- Wider `setjmp` positions if needed (today: if/switch condition, or a declaration/assignment of
  its result — the canonical idioms); `trigraphs`; true extended `long double` precision.
