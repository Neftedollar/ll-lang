# ll-lang Language Specification

**Version:** 1.0 (Phase 10)  **Extension:** `.lll`  **Encoding:** UTF-8, ASCII-only operators  **Tests:** see CI

---

## 1. Overview

ll-lang is a statically-typed functional language compiled to F# (default), TypeScript, Python, Java, C#, and LLVM IR. Optimized for LLM code generation: minimal keywords, zero ceremony, maximum token efficiency.

**Properties:**
- 15 keywords — declarations use the uppercase/lowercase convention, not reserved words
- Hindley-Milner inference — annotate function parameters; everything else is inferred
- Compiled = works — tag violations, unbound variables, non-exhaustive matches, unit mismatches are compile-time errors
- LLM-readable errors — compact one-line format: `E001 12:5 TypeMismatch expected:UserId got:Int`
- Pure functional — no mutable state, no exceptions, no null
- Bootstrap complete — `compiler₁.fs == compiler₂.fs` (self-hosted, 2900+ LOC bootstrap compiler)
- Token-efficient — 8–17% more compact than F# on real code; 1.3–5.9× more compact than TypeScript/Python/Java on type definitions

---

## 2. Lexical Structure

**Indentation:** Significant. 2 or 4 spaces, consistent per file. Never tabs. Lexer emits synthetic `INDENT`/`DEDENT` tokens.

**Comments:** `-- text to end of line`

**Keywords (15):** `match  if  else  import  export  module  trait  impl  external  opaque  tag  unit  true  false  let`

The word `let` is reserved for local bindings inside expressions (`let x = e`) and at top-level. The words `fn`, `type`, `in`, `then`, `with` are not keywords; bare declaration forms are idiomatic.

**Identifiers:**
- Value/function: `[a-z_][a-zA-Z0-9_]*` — `x`, `mapSize`, `_unused`
- Type/constructor/module: `[A-Z][a-zA-Z0-9_]*` — `Int`, `Some`, `Std`
- `external` declarations may use mixed-case foreign symbol names (for example `JSON_parse`)

**Literals:** `42` (Int), `3.14` (Float), `"hello"` (Str), `true`/`false` (Bool), `'a'` (Char)

---

## 3. Types

### Primitives

`Int`  `Float`  `Str`  `Bool`  `Char`  `Unit`

### Composite

```lll
[1 2 3]          -- List[Int]
[1; 2; 3]        -- List[Int]  (semicolon-separated variant)
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
| `external name(p T) Ret` | Foreign function declaration (no body) |
| `opaque Type[A]` | Opaque host type (runtime-erased handle) |
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

-- External/opaque FFI surface
opaque Any
opaque Promise[A]
opaque Response
external fetch(url Str) Promise[Response]
external JSON_parse(s Str) Any

-- Trait and impl
trait Show A =
  show(a A) Str

impl Show Int =
  show(n Int) Str = intToStr n
```

### External mapping status

`external` is backend-aware in codegen and validated during compilation.
Unknown declarations now produce `E026 UnknownExternalMapping` before emit.

| Backend | Known mappings |
|---------|----------------|
| F# (`Codegen.fs`) | `console_log`, `JSON_parse` |
| Python (`CodegenPy.fs`) | `console_log`, `JSON_parse` |
| TypeScript (`CodegenTS.fs`) | `console_log`, `JSON_parse`, `fetch` |
| Java (`CodegenJava.fs`) | `console_log` |
| C# (`CodegenCSharp.fs`) | `console_log`, `JSON_parse` |
| LLVM (`CodegenLLVM.fs`) | `console_log` |

At the compile stage, each backend target checks whether every `external` name is
known. If a mapping is missing, compilation fails with:
`E026 line:col UnknownExternalMapping target:<target> name:<name>`.

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
| Boolean not | `!expr` | `!isReady` |
| Cons | `x :: xs` | `1 :: [2 3]` |
| List | `[a b c]` or `[a; b; c]` | `[1 2 3]`, `[1; 2; 3]` |
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

**`lll.toml`** — project manifest at the project root (legacy `ll.toml` is still accepted):

```toml
[project]
name = "myapp"
version = "0.8.0"

[deps]
std = { path = "../stdlib" }

[platform]
use = ["fsharp", "typescript"]   -- emit to multiple targets at once
```

**CLI:**

```bash
lllc new myapp       # scaffold: lll.toml + src/Main.lll
lllc build           # compile project (topo-sorted)
lllc build --target ts  # compile to TypeScript
lllc install         # fetch source-based dependencies
lllc run src/Main.lll
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
| `--target cs` | C# | Compile-safe MVP skeleton backend |
| `--target llvm` | LLVM IR | Deterministic IR stub backend (experimental subset in 1.0) |

1.0 compatibility guarantees for stable vs experimental surface are defined in
`docs/release-contract-1.0.md`.

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
| `E026` | `UnknownExternalMapping` | External name has no backend mapping |
| `E020` | `ModulePathMismatch` | `module` header does not match file path under `src/` |
| `E024` | `ModuleCycle` | Circular module dependency |
| `E025` | `NoProjectForImport` | Non-`Std.*` import used in single-file mode (no `lll.toml`) |

```
E001 12:5  TypeMismatch   expected:Str[UserId] got:Str   hint:wrap:UserId
E003 15:1  NonExhaustiveMatch  type:Shape missing:Empty
E008 3:1   InfiniteType   var:a cycle:a=List[a]
```

Invalid example files declare `-- expect: E003` on line 1; the test runner asserts exactly that code.

---

## 11. Standard Library

Self-hosted modules live under `stdlib/src`. Prelude is always in scope; additional modules are imported via `import Std.X`.

**Prelude (always in scope):**

Core (no type dependencies):
```
abs          : Int -> Int
absf         : Float -> Float
sqrt         : Float -> Float
min          : Int -> Int -> Int
max          : Int -> Int -> Int
listLen      : List[A] -> Int
listMap      : (A -> B) -> List[A] -> List[B]
listFilter   : (A -> Bool) -> List[A] -> List[A]
listFold     : (B -> A -> B) -> B -> List[A] -> B
listReverse  : List[A] -> List[A]
listAppend   : List[A] -> List[A] -> List[A]
listConcat   : List[List[A]] -> List[A]
listIsEmpty  : List[A] -> Bool
strLen       : Str -> Int
strConcat    : Str -> Str -> Str
strTrim      : Str -> Str
strContains  : Str -> Str -> Bool
strSplit     : Str -> Str -> List[Str]
strSlice     : Str -> Int -> Int -> Str
strIndexOf   : Str -> Str -> Int
strChars     : Str -> List[Char]
strFromChars : List[Char] -> Str
strReverse   : Str -> Str
intToStr     : Int -> Str
floatToStr   : Float -> Str
intToChar    : Int -> Char
charToInt    : Char -> Int
charIsDigit  : Char -> Bool
charIsAlpha  : Char -> Bool
charIsSpace  : Char -> Bool
print        : Str -> Unit
printfn      : Str -> Unit
readFile     : Str -> Str
writeFile    : Str -> Str -> Unit
fileExists   : Str -> Bool
exit         : Int -> Unit
```

Maybe-dependent (requires `Maybe A = Some A | None` in scope):
```
listHead        : List[A] -> Maybe[A]
listTail        : List[A] -> Maybe[List[A]]
listAt          : List[A] -> Int -> Maybe[A]
maybeMap        : (A -> B) -> Maybe[A] -> Maybe[B]
maybeBind       : Maybe[A] -> (A -> Maybe[B]) -> Maybe[B]
maybeWithDefault: A -> Maybe[A] -> A
strToInt        : Str -> Maybe[Int]
strToFloat      : Str -> Maybe[Float]
```

Result-dependent (requires `Result A E = Ok A | Err E` in scope):
```
resultMap    : (A -> B) -> Result[A][E] -> Result[B][E]
resultBind   : Result[A][E] -> (A -> Result[B][E]) -> Result[B][E]
resultMapErr : (E -> F) -> Result[A][E] -> Result[A][F]
```

**Stdlib modules:** `Std.Maybe`, `Std.Map` (red-black tree), `Std.Toml`, `Std.Json`, `Std.Lexer`, `Std.Parser`, `Std.Elaborator`, `Std.Codegen`, `Std.CodegenTS`, `Std.CodegenPy`, `Std.CodegenJava`, `Std.CodegenLLVM`, `Std.Render`, `Std.Test`, `Std.Compiler`.

---

## 12. Reserved Words and Operators

**Keywords (15):** `match  if  else  import  export  module  trait  impl  external  opaque  tag  unit  true  false  let`

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
| `let x = 1 in ...` | bare `x = 1` layout-based local bindings |
| `[1, 2, 3]` | `[1 2 3]` or `[1; 2; 3]` (commas make tuples) |
| mutable state | thread state through return values |
| throw/raise exceptions | return `Result A E` or `Maybe A` |
| Unicode operators | ASCII only: `->` not `→` |
