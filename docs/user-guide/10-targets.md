# Compilation Targets

ll-lang supports multiple output targets. In 1.0, stable targets are F#, TypeScript, Python, Java, and C#. LLVM IR is available as an experimental subset backend.

See [Release Contract 1.0](../release-contract-1.0.md) for compatibility guarantees.

## Selecting a Target

Use the `--target` flag with `lllc build`:

```
lllc build --target fs  file.lll   # F# (default)
lllc build --target ts  file.lll   # TypeScript
lllc build --target py  file.lll   # Python
lllc build --target java file.lll  # Java
lllc build --target cs  file.lll   # C#
lllc build --target llvm file.lll  # LLVM IR (experimental subset)
```

Accepted aliases: `fs` / `fsharp`, `ts` / `typescript`, `py` / `python`, `java` / `jvm`, `cs` / `csharp`, `llvm` / `ll`.

## Stability in 1.0

- Stable targets: `fs`, `ts`, `py`, `java`, `cs`
- Experimental target: `llvm`

The output file uses the appropriate extension (`.fs`, `.ts`, `.py`, `.java`, `.cs`, `.ll`).

## F# (default)

The F# backend is the primary target and the most complete. All language features are supported. The output is valid F# 8+ with discriminated unions, computation expressions, and `[<EntryPoint>]`.

```
lllc build file.lll          # emits file.fs
lllc build --target fs file.lll  # same
```

F# stdlib helpers are emitted on demand. If a module does not reference ll-lang stdlib symbols, no prelude block is inserted.

## TypeScript

The TypeScript backend emits ES2020+ TypeScript. Requires TypeScript 4.4+ for `const` assertions.

```
lllc build --target ts file.lll  # emits file.ts
```

### Type mapping

| ll-lang   | TypeScript         |
|-----------|--------------------|
| `Int`     | `number`           |
| `Float`   | `number`           |
| `Str`     | `string`           |
| `Bool`    | `boolean`          |
| `Char`    | `string`           |
| `Unit`    | `void`             |
| `List[A]` | `A[]`              |
| `Maybe[A]`| `A \| null`        |

### Sum types

Sum types become TypeScript discriminated unions:

```ll-lang
Shape = Circle Float | Rect Float Float | Empty
```

Emits:

```typescript
type Shape =
  { _tag: `Circle`; _0: number }
  | { _tag: `Rect`; _0: number; _1: number }
  | { _tag: `Empty` };

const Circle = (_0: number): Shape => ({ _tag: `Circle` as const, _0 });
const Rect = (_0: number, _1: number): Shape => ({ _tag: `Rect` as const, _0, _1 });
const Empty: Shape = { _tag: `Empty` as const };
```

### Functions

Functions are curried arrow functions:

```ll-lang
add(a Int)(b Int) = a + b
```

Emits:

```typescript
const add = (a: number) => (b: number): number => a + b;
```

### Pattern matching

Match expressions become `switch`-like if/else chains on `._tag`.

### Stdlib

The TypeScript backend emits a compact stdlib helper block on demand: only helpers that are actually referenced in the module/project are generated once as `const` bindings, then reused at call sites.

## Python

The Python backend emits Python 3.10+ with `from __future__ import annotations`.

```
lllc build --target py file.lll  # emits file.py
```

### Type mapping

| ll-lang   | Python             |
|-----------|--------------------|
| `Int`     | `int`              |
| `Float`   | `float`            |
| `Str`     | `str`              |
| `Bool`    | `bool`             |
| `Char`    | `str`              |
| `Unit`    | `None`             |
| `List[A]` | `list[A]`          |
| `Maybe[A]`| `Optional[A]`      |

### Sum types

Sum types become immutable `@dataclass(frozen=True)` classes with a `_tag` field, collected into a `Union` type alias:

```ll-lang
Shape = Circle Float | Rect Float Float | Empty
```

Emits:

```python
@dataclass(frozen=True)
class Circle:
    _tag: str = "Circle"
    _0: float

@dataclass(frozen=True)
class Rect:
    _tag: str = "Rect"
    _0: float
    _1: float

@dataclass(frozen=True)
class Empty:
    _tag: str = "Empty"
    pass

Shape = Union[Circle, Rect, Empty]
```

### Functions

Curried functions become nested `def` statements with `return`:

```ll-lang
add(a Int)(b Int) = a + b
```

Emits:

```python
def add(a: int):
    def _f_b(b: int):
        return a + b
    return _f_b
```

### Pattern matching

Match expressions become ternary if/else chains (Python expressions, not statements).

### Stdlib

The Python backend always emits typing/import headers, and emits runtime stdlib helpers on demand. Modules that do not reference stdlib helpers avoid runtime-prelude bloat.

### External mappings

Python also maps `external` declarations to standard library functions. In this release `JSON_parse` is emitted as `json.loads`, and `import json` is added only when the module/project uses `JSON_parse`.

## Java

The Java backend emits Java source with a static-class style runtime prelude, included on demand when stdlib helpers are referenced.

```bash
lllc build --target java file.lll  # emits file.java
# CLI also prints SDK commands, e.g.:
#   suggested compile: javac /.../Main.java
#   suggested run: javac /.../Main.java && java Main
```

## C#

The C# backend emits idiomatic C# with platform types (`List<T>`, `Func<...>`), record/interface ADTs, and a compact runtime prelude.
The focus is low-boilerplate target code that still compiles cleanly in generated SDK projects, including self-hosted stdlib artifacts.
The runtime prelude is emitted on demand: modules that do not reference stdlib helpers avoid prelude bloat.

```bash
lllc build --target cs file.lll  # emits file.cs
```

### External mappings

The C# backend maps `external` declarations to runtime and base-library APIs. `JSON_parse` currently uses `System.Text.Json.JsonSerializer.Deserialize<object>`, and `using System.Text.Json;` is emitted only when needed.

## LLVM IR (Experimental)

The LLVM backend emits typed LLVM IR with explicit control-flow lowering (`if`, `match`) and `main` lowering suitable for `llvm-as` validation.
Current supported subset includes integer/float arithmetic (`add/sub/mul/div`), integer comparisons, direct calls, `let`-style local bindings, ADT constructor allocation, and list literal/cons lowering via a compact runtime node ABI.

```bash
lllc build --target llvm file.lll  # emits file.ll (experimental subset)
```

### External mappings

LLVM currently supports `external console_log(msg Str) Unit` via direct symbol declaration/call (`@console_log(ptr)`).

## Limitations

Known gaps:

- **Trait impls** are emitted as standalone functions with a type-name prefix.
- **Tag types** (`Str[UserId]`) are erased to the base type at the target.
- **LLVM IR** is still subset-based (full trait/ADT parity is not complete yet).

## Running Multi-Target Output

### TypeScript

```bash
lllc build --target ts hello.lll
# SDK run command mirrors CLI `run --target ts`:
npx tsc hello.ts --target es2022 --module esnext && node hello.js
```

### Python

```bash
lllc build --target py hello.lll
python hello.py
```

### Java / C#

```bash
lllc build --target java hello.lll
## if module tail is Hello, class-aligned mirror is written as Hello.java
javac Hello.java && java Hello
lllc build --target cs hello.lll
```
