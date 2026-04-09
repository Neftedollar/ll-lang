# Compilation Targets

ll-lang supports multiple output targets. The same source compiles to F#, TypeScript, or Python.

## Selecting a Target

Use the `--target` flag with `lllc build`:

```
lllc build --target fs  file.lll   # F# (default)
lllc build --target ts  file.lll   # TypeScript
lllc build --target py  file.lll   # Python
```

Accepted aliases: `fs` / `fsharp`, `ts` / `typescript`, `py` / `python`.

The output file uses the appropriate extension (`.fs`, `.ts`, `.py`).

## F# (default)

The F# backend is the primary target and the most complete. All language features are supported. The output is valid F# 8+ with discriminated unions, computation expressions, and `[<EntryPoint>]`.

```
lllc build file.lll          # emits file.fs
lllc build --target fs file.lll  # same
```

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
type Shape = Circle Float | Rect Float Float | Empty
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
fn add(a Int)(b Int) Int = a + b
```

Emits:

```typescript
const add = (a: number) => (b: number): number => a + b;
```

### Pattern matching

Match expressions become `switch`-like if/else chains on `._tag`.

### Stdlib

The TypeScript prelude is appended at the top of the output. It provides the ll-lang standard library as TypeScript functions.

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

Sum types become `@dataclass` classes with a `_tag` field, collected into a `Union` type alias:

```ll-lang
type Shape = Circle Float | Rect Float Float | Empty
```

Emits:

```python
@dataclass
class Circle:
    _tag: str = "Circle"
    _0: float

@dataclass
class Rect:
    _tag: str = "Rect"
    _0: float
    _1: float

@dataclass
class Empty:
    _tag: str = "Empty"
    pass

Shape = Union[Circle, Rect, Empty]
```

### Functions

Curried functions become nested `def` statements with `return`:

```ll-lang
fn add(a Int)(b Int) Int = a + b
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

The Python prelude is prepended to the output. It provides all ll-lang stdlib functions as Python lambdas and defs, plus `from __future__ import annotations`, `dataclasses`, `typing`, `sys`, and `math` imports.

## Limitations

The current multi-target backends cover all core language features. Known gaps:

- **Trait impls** are emitted as standalone functions with the type name prefix (e.g. `maybe_map`).
- **Tag types** (`Str[UserId]`) are erased to the base type at the target.
- **Java** target is planned but not yet implemented.

## Running Multi-Target Output

### TypeScript

```bash
lllc build --target ts hello.lll
npx ts-node hello.ts
# or: tsc hello.ts && node hello.js
```

### Python

```bash
lllc build --target py hello.lll
python hello.py
```
