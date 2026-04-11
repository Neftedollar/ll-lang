# ll-lang Language Specification

**Version:** 1.0 (Phase 10)  **Extension:** `.lll`  **Encoding:** UTF-8, ASCII-only operators

---

## 1. Overview

ll-lang is a statically-typed functional language compiled to F# (default), TypeScript, Python, and Java. Optimized for LLM code generation: minimal keywords, zero ceremony, maximum token efficiency.

**Properties:**
- 12 keywords — declarations use the uppercase/lowercase convention, not reserved words
- Hindley-Milner inference — annotate function parameters; everything else is inferred
- Compiled = works — tag violations, unbound variables, non-exhaustive matches, unit mismatches are compile-time errors
- LLM-readable errors — compact one-line format: `E001 12:5 TypeMismatch expected:UserId got:Int`
- Pure functional — no mutable state, no exceptions, no null
- Bootstrap complete — `compiler₁.fs == compiler₂.fs` (self-hosted, 529 tests)

---

## 2. Lexical Structure

**Indentation:** Significant. 2 or 4 spaces, consistent per file. Never tabs. Lexer emits synthetic `INDENT`/`DEDENT` tokens.

**Comments:** `-- text to end of line`

**Keywords (12):** `match  if  else  import  export  module  trait  impl  tag  unit  true  false`

The words `fn`, `type`, `let`, `in`, `then`, `with` are not keywords; bare declaration forms are idiomatic.

**Identifiers:**
- Value/function: `[a-z_][a-zA-Z0-9_]*` — `x`, `mapSize`, `_unused`
- Type/constructor/module: `[A-Z][a-zA-Z0-9_]*` — `Int`, `Some`, `Std`

**Literals:** `42` (Int), `3.14` (Float), `"hello"` (Str), `true`/`false` (Bool), `'a'` (Char)

---

## 3. Types

### Primitives

`Int`  `Float`  `Str`  `Bool`  `Char`  `Unit`

### Composite

```lll
[1 2 3]          -- List[Int]  (space-separated elements)
1, "hello"       -- (Int, Str) tuple  (comma forms tuples)
Int -> Int -> Bool  -- function type (right-associative)
List[A]  RBMap[K][V]  -- parametric: brackets apply type args
```

### User-defined

```lll
-- Sum type (ADT)
Shape = Circle Float | Rect Float Float | Empty
Maybe A = Some A | None
Result A E = Ok A | Err E

-- Multi-line sum
Token =
  | KwIf | KwElse | KwMatch
  | Ident Str | IntLit Int

-- Record
Point = x Float, y Float

-- Phantom type (distinct at compile time, same at runtime)
Email[state] = Str
```

### Tags and units

`tag T` — zero-cost semantic label. `value[T]` applies it. Tagged types are distinct (`Str[UserId] ≠ Str[Email]`). E005 on mismatch.

`unit T` — physical unit. Numeric tags participate in algebra:

```lll
tag UserId;  tag Email
unit m;  unit s

uid = "user-42"[UserId]         -- Str[UserId]
speed(d Float[m])(t Float[s]) = d / t  -- return: Float[m/s]
```

Unit rules: `Float[m] + Float[m]` → `Float[m]` (same required, E004 if different); `/` and `*` compose units.

---

## 4. Declarations

| Pattern | Meaning |
|---------|---------|
| `Uppercase A = ...` | Type declaration |
| `lowercase(p T)(q T) = expr` | Function declaration |
| `lowercase = expr` | Value binding (top-level: `let` optional) |
| `tag Name` | Tag declaration |
| `unit Name` | Unit declaration |
| `trait Name T = ...` | Trait declaration |
| `impl Trait Type = ...` | Trait implementation |
| `module Path.Name` | Module header |
| `import Path.Name` | Import |
| `export decl` | Export |

```lll
-- Function: each param in its own parens; return type inferred
add(a Int)(b Int) = a + b
add(a Int)(b Int) Int = a + b  -- explicit return (optional)

-- Multi-line body via indent
clamp(x Int)(lo Int)(hi Int) =
  if x < lo
    lo
  else if x > hi
    hi
  else x

-- No-arg function
main() = 0

-- Local bindings: last expr is the return value
process(xs List[Int]) =
  doubled = listMap (\x. x * 2) xs
  listLen doubled

-- Trait and impl
trait Show A =
  show(a A) Str

impl Show Int =
  show(n Int) Str = intToStr n
```

---

## 5. Expressions

| Form | Syntax | Example |
|------|--------|---------|
| Literal | `42`, `"hi"`, `true` | — |
| Variable | `x` | — |
| Constructor | `Some`, `Circle` | — |
| Application | juxtaposition | `add 3 4`, `mapLookup cmp k m` |
| Pipe | `expr -> f -> g` | `xs -> listMap f -> listLen` |
| Arithmetic | `+  -  *  /  ^` | `a * b + c` |
| Comparison | `==  !=  <  >  <=  >=` | `x == 0` |
| Cons | `x :: xs` | `1 :: [2 3]` |
| List | `[a b c]` | `[1 2 3]` |
| Tuple | `a, b` | `x, y` |
| Tagged | `value[Tag]` | `"u1"[UserId]` |
| Lambda | `\x. body` | `\a. \b. a + b` |
| If | `if c` / indent / `body` / `else alt` | see below |
| Match | `match e` / indent / `\| P -> e` | see below |
| Local binding | `x = expr` (in block) | `y = f 5` |

**If:**

```lll
if x < 0
  0 - x
else x
```

**Match:**

```lll
match shape
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

**Clause sugar** — function body starting with `|` implicitly matches the last parameter:

```lll
area(s Shape) =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0
```

**Lambda multi-param shorthand:** `\acc k v. acc + k`

**Sequential effects:** bind unused result to `_`:

```lll
main() =
  _ = printfn "step 1"
  _ = printfn "step 2"
  0
```

---

## 6. Patterns

| Pattern | Syntax | Example |
|---------|--------|---------|
| Wildcard | `_` | `| _ -> 0` |
| Variable | `x` | `| x -> x + 1` |
| Literal | `42`, `"hi"` | `| 0 -> "zero"` |
| Constructor | `Ctor fields` | `| Some v -> v` |
| Nested | `Ctor (Ctor2 x) y` | `| Node (Red) l k v r -> ...` |
| Cons | `h :: t` | `| h :: t -> h` |
| Nil | `[]` | `| [] -> 0` |
| Tuple | `a, b` | `| x, y -> x + y` |

All matches must be exhaustive (E003 if not). Use `| _ -> ...` for catch-all.

---

## 7. Type System

**Hindley-Milner (Algorithm W) with let-generalization.**

- Annotate: function parameters `(name Type)` — always required
- Omit: return types, local bindings, lambda params when inferable

```lll
-- params annotated, return inferred
mapSize(m RBMap[K][V]) =
  match m
    | Leaf -> 0
    | Node _ left _ _ right -> 1 + mapSize left + mapSize right
```

**Parametric types** — type params after the name:

```lll
Maybe A = Some A | None         -- bare type variable
Email[state] = Str              -- phantom bracket param
mapLookup(cmp K -> K -> Int)(k K)(m RBMap[K][V]) = ...
```

**Trait constraints** — `[F: Trait]` before parameters:

```lll
transform[F: Functor](xs F[A])(f A -> B) = map f xs
printVal[A: Show](x A) = printfn (show x)
```

**Higher-kinded types** — traits range over type constructors (`F` has kind `* → *`):

```lll
trait Functor F =
  map(f A -> B)(fa F[A]) F[B]
```

**No null, no exceptions** — use `Maybe A` and `Result A E`.

---

## 8. Module System

Every `.lll` file starts with a module declaration on line 1:

```lll
module Std.Map

import Std.List

export mapInsert(cmp K -> K -> Int)(k K)(v V)(m RBMap[K][V]) = ...
```

Module path (`Std.Map`) must match file path under `src/` (`src/Map.lll`).

`import` brings exported names into scope. `export` marks declarations public; all others are private.

**`ll.toml`** — project manifest at the project root:

```toml
[project]
name = "myapp"

[deps]
std = { path = "../stdlib" }
```

**CLI:**

```bash
lllc new myapp       # scaffold: ll.toml + src/Main.lll
lllc build           # compile project (topo-sorted)
lllc build --target ts  # compile to TypeScript
lllc install         # fetch source-based dependencies
lllc run src/Main.lll
lllc check src/Main.lll
lllc mcp             # start MCP server (stdio, 10 tools)
```

---

## 9. Compilation Targets

| Flag | Target | Output style |
|------|--------|-------------|
| `--target fs` (default) | F# | Discriminated unions |
| `--target ts` | TypeScript | Sealed interfaces |
| `--target py` | Python | `@dataclass` + `Union` |
| `--target java` | Java 21 | Sealed interfaces + records |

---

## 10. Error Codes

Format: `EXXX line:col ErrorKind details`  — one line, regex-parseable.

| Code | Name | Cause |
|------|------|-------|
| `E001` | `TypeMismatch` | Expected type A, got B |
| `E002` | `UnboundVar` | Identifier not in scope |
| `E003` | `NonExhaustiveMatch` | Match missing constructors |
| `E004` | `UnitMismatch` | Incompatible units |
| `E005` | `TagViolation` | Wrong or missing tag |
| `E007` | `PlatformMismatch` | Module requires unsupported target |
| `E008` | `InfiniteType` | Occurs-check failure |
| `E020` | `ModuleNotFound` | Import cannot be resolved |
| `E024` | `CyclicImport` | Circular module dependency |

```
E001 12:5  TypeMismatch   expected:Str[UserId] got:Str   hint:wrap:UserId
E003 15:1  NonExhaustiveMatch  type:Shape missing:Empty
E008 3:1   InfiniteType   var:a cycle:a=List[a]
```

Invalid example files declare `-- expect: E003` on line 1; the test runner asserts exactly that code.

---

## 11. Standard Library

Self-hosted (10 modules, ~5857 LOC). Prelude is always in scope; additional modules via `import Std.X`.

**Prelude (selected):**

```
printfn      : Str -> Unit
listMap      : (A -> B) -> List[A] -> List[B]
listFold     : (B -> A -> B) -> B -> List[A] -> B
listFilter   : (A -> Bool) -> List[A] -> List[A]
listLen      : List[A] -> Int
listAppend   : List[A] -> List[A] -> List[A]
listAt       : List[A] -> Int -> Maybe[A]
listHead     : List[A] -> Maybe[A]
listReverse  : List[A] -> List[A]
strConcat    : Str -> Str -> Str
strLen       : Str -> Int
strSplit     : Str -> Str -> List[Str]
strTrim      : Str -> Str
intToStr     : Int -> Str
floatToStr   : Float -> Str
charToInt    : Char -> Int
charIsDigit  : Char -> Bool
strToInt     : Str -> Maybe[Int]
maybeMap     : (A -> B) -> Maybe[A] -> Maybe[B]
maybeDefault : A -> Maybe[A] -> A
resultMap    : (A -> B) -> Result[A][E] -> Result[B][E]
resultBind   : (A -> Result[B][E]) -> Result[A][E] -> Result[B][E]
```

**Stdlib modules:** `Std.Map` (red-black tree), `Std.Toml`, `Std.Lexer`, `Std.Parser`, `Std.Elaborator`, `Std.Codegen`, `Std.CodegenTS`, `Std.CodegenPy`, `Std.CodegenJava`, `Std.Compiler`.

---

## 12. Reserved Words and Operators

**Keywords (12):** `match  if  else  import  export  module  trait  impl  tag  unit  true  false`

**Operators:**
```
+  -  *  /  ^         arithmetic
==  !=  <  >  <=  >=  comparison
->                    pipe / type arrow / match branch
\  .                  lambda
,                     tuple
|                     sum type / match arm
::                    cons
=                     binding / definition
_                     wildcard
[ ]  ( )              brackets / grouping
```

**Anti-patterns:**

| Do NOT | Do instead |
|--------|-----------|
| `type Maybe A = ...` | `Maybe A = Some A \| None` |
| `fn add(a Int)(b Int) = ...` | `add(a Int)(b Int) = a + b` |
| `if cond then body` | `if cond` / indent / `body` |
| `match x with \| ...` | `match x` / indent / `\| ...` |
| `let x = 1 in x + 1` | layout-based local bindings |
| `[1, 2, 3]` | `[1 2 3]` (commas make tuples) |
| mutable state | thread state through return values |
| throw/raise exceptions | return `Result A E` or `Maybe A` |
| Unicode operators | ASCII only: `->` not `→` |
