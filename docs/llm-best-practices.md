# ll-lang Best Practices for LLMs

## 1. Language Overview

ll-lang is a statically-typed functional language compiled to F# (also TS, Python, Java). It was designed for LLM code generation: minimal keywords, zero syntactic noise, maximal token efficiency.

Key properties:
- 12 keywords: `match if else import export module trait impl tag unit true false`
- No `fn`, `type`, `let`, `in`, `then`, `with` needed for most declarations
- Hindley-Milner type inference -- annotate only when needed
- Significant indentation (2 or 4 spaces, consistent per file)
- ASCII-only operators, no Unicode
- Pure functional -- no mutable state, no exceptions
- File extension: `.lll`

The compiler is an oracle: if it compiles, it works. Use the MCP server for instant feedback.

## 2. Syntax Quick Reference

| Construct | Syntax | Example |
|-----------|--------|---------|
| Type (sum) | `Name A = Ctor1 A \| Ctor2` | `Maybe A = Some A \| None` |
| Type (record) | `Name = f1 T1, f2 T2` | `Point = x Float, y Float` |
| Function | `name(p T)(q T) = expr` | `add(a Int)(b Int) = a + b` |
| Binding | `name = expr` | `x = 42` |
| Let binding | `let name = expr` | `let pi = 3.14159` |
| Lambda | `\params. body` | `\x. x * 2` |
| If | `if cond` / `  body` / `else alt` | see below |
| Match | `match expr` / `  \| Pat -> body` | see below |
| Clause sugar | `name(params) =` / `  \| Pat -> body` | match on last arg implicitly |
| Pipe | `expr -> f -> g` | `xs -> listMap double -> listLen` |
| Tag literal | `value[Tag]` | `"user-42"[UserId]` |
| List | `[a b c]` | `[1 2 3]` (space-separated) |
| Tuple | `a, b, c` | `1, "hello"` |
| Comment | `-- text` | `-- this is a comment` |
| Module | `module Path.Name` | `module Std.Map` |
| Import | `import Path.Name` | `import Std.List` |
| Export | `export decl` | `export greet(name Str) = ...` |
| Trait | `trait Name T = ...` | see section below |
| Impl | `impl Trait Type = ...` | see section below |
| Tag decl | `tag Name` | `tag UserId` |
| Unit decl | `unit Name` | `unit m` |

## 3. Declaration Patterns

### Types -- start with Uppercase, no `type` keyword

```lll
-- Sum type (single line)
Shape = Circle Float | Rect Float Float | Empty

-- Sum type (multi-line)
Token =
  | TIdent Str
  | TNum Str
  | TLParen
  | TRParen

-- Parametric sum
Result A E = Ok A | Err E

-- Record (product) type
Point = x Float, y Float

-- Phantom type parameter
Email[state] = Str
```

### Functions -- lowercase + parens, no `fn` keyword

```lll
-- Each param in its own parens: (name Type)
add(a Int)(b Int) = a + b

-- Multi-line body: indent
clamp(x Int)(lo Int)(hi Int) =
  if x < lo
    lo
  else if x > hi
    hi
  else x

-- Return type inferred. Annotate only when ambiguous.
square(x Int) = x * x

-- No-arg function
main() = 0
```

The `fn` keyword exists but is optional for declarations. Prefer the bare form.

### Bindings -- lowercase + `=`

```lll
-- Top-level (use `let` for constants without params)
let pi = 3.14159

-- Local bindings (no keyword needed inside function bodies)
example =
  y = double 5
  y + 1
```

### Lambdas -- `\params. body`

```lll
let triple = \x. x * 3
let add = \a. \b. a + b

-- Multi-param shorthand
listFold (\acc k v. acc + k) 0 m
```

## 4. Type Inference

ll-lang uses Hindley-Milner inference. Rules:

**Omit types when:**
- Return type is obvious from the body
- Local bindings (always inferred)
- Lambda parameters when context determines type

**Annotate when:**
- Function parameters (always required in `(name Type)` form)
- Polymorphic types need disambiguation
- Complex higher-order function signatures for readability

```lll
-- Params annotated, return inferred
mapSize(m RBMap[K][V]) =
  match m
    | Leaf -> 0
    | Node _ left _ _ right -> 1 + mapSize left + mapSize right

-- Type params on parametric types use brackets
mapLookup(cmp K -> K -> Int)(k K)(m RBMap[K][V]) = ...
```

Function type syntax in annotations: `A -> B -> C` (right-associative arrows). Parametric types: `List[A]`, `RBMap[K][V]`.

## 5. Pattern Matching Idioms

### Explicit match

```lll
mapLookup(cmp K -> K -> Int)(k K)(m RBMap[K][V]) =
  match m
    | Leaf -> None
    | Node _ left nk nv right ->
      d = cmp k nk
      if d < 0
        mapLookup cmp k left
      else if d > 0
        mapLookup cmp k right
      else Some nv
```

### Clause sugar -- implicit match on last argument

When a function body starts with `|` arms, it desugars to `match` on the last parameter:

```lll
area(s Shape) =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0

-- Equivalent to:
area(s Shape) =
  match s
    | Circle r -> 3.14159 * r * r
    | Rect w h -> w * h
    | Empty -> 0.0
```

### Nested match

```lll
balanceLeft(left RBMap[K][V])(k K)(v V)(right RBMap[K][V]) =
  match left
    | Node (Red) ll lk lv lr ->
      match ll
        | Node (Red) a xk xv b ->
          Node (Red) (Node (Black) a xk xv b) lk lv (Node (Black) lr k v right)
        | _ ->
          match lr
            | Node (Red) b yk yv c ->
              Node (Red) (Node (Black) ll lk lv b) yk yv (Node (Black) c k v right)
            | _ -> Node (Black) left k v right
    | _ -> Node (Black) left k v right
```

### Match in binding

```lll
ok1 = match r1
  | Some v -> v == "three"
  | None -> false
```

### Pattern types

- Constructor: `Some a`, `Node color left key val right`
- Wildcard: `_`
- Variable: `x` (binds the value)
- Literal: `0`, `"hello"`, `true`
- Nested: `Node (Red) _ _ _ _`

**All matches must be exhaustive.** The compiler emits E003 for missing cases.

## 6. Common Patterns

### Error handling with Result/Maybe

```lll
Maybe A = Some A | None
Result A E = Ok A | Err E

safeDivide(a Float)(b Float) =
  if b == 0.0
    None
  else Some (a / b)

parseAge(s Str) =
  if s == ""
    Err "empty input"
  else Ok 42
```

### Recursive data structures

```lll
RBMap K V = Leaf | Node Color (RBMap K V) K V (RBMap K V)

mapFold(f B -> K -> V -> B)(acc B)(m RBMap[K][V]) =
  match m
    | Leaf -> acc
    | Node _ left k v right ->
      acc1 = mapFold f acc left
      acc2 = f acc1 k v
      mapFold f acc2 right
```

### List operations with stdlib

```lll
sumList(xs List[Int]) = listFold (\acc. \x. acc + x) 0 xs
doubleAll(xs List[Int]) = listMap double xs
```

### Pipe chains

```lll
firstDoubled(xs List[Int]) =
  xs -> head -> map (\x. x * 2)
```

### Sequential effects (threading state)

```lll
main() =
  _ = printfn "step 1"
  _ = printfn "step 2"
  0
```

Bind unused results to `_` to sequence side effects.

### Tags for type safety

```lll
tag UserId
tag Email

getUser(id Str[UserId]) = Some "alice"
sendEmail(to Str[Email]) = to

-- Create tagged values
let uid = "user-42"[UserId]
```

### Unit algebra

```lll
tag m
tag s

speed(d Float[m])(t Float[s]) = d / t
-- Compiler infers return type: Float[m/s]
```

### Traits and impls

```lll
trait Functor F =
  map(f A -> B)(fa F[A]) F[B]

impl Functor Maybe =
  map(f A -> B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None

-- Constrained function
transform[F: Functor](xs F[A])(f A -> B) = map f xs
```

### Modules

```lll
module MyApp.Parser

import Std.List
import Std.Maybe

export parseInput(s Str) = ...
```

Module path must match file location in `src/`. E.g., `module MyApp.Parser` lives at `src/Parser.lll`.

## 7. Anti-patterns

| Do NOT | Do instead |
|--------|-----------|
| Use `type` keyword | `Maybe A = Some A \| None` (Uppercase starts type) |
| Use `fn` keyword | `add(a Int)(b Int) = a + b` (bare declaration) |
| Use `let` for functions | `let` is for simple bindings only; use `name(params) = body` |
| Write `then` after `if` | `if cond` / newline+indent / `body` |
| Write `with` after `match` | `match expr` / newline+indent / `\| Pat -> body` |
| Use mutable state | Thread state through function params and return values |
| Throw exceptions | Return `Result A E` or `Maybe A` |
| Use braces `{}` | Indentation-based blocks only |
| Use semicolons | Newlines separate expressions |
| Over-annotate types | Let HM inference work; annotate params, not returns |
| Write `in` after `let` | Bindings chain via layout: `x = 1` newline `y = x + 1` |
| Use Unicode operators | ASCII only: `->` not arrow symbols |

### Common mistakes

- **Forgetting exhaustive patterns**: every `match` must cover all constructors. Add `| _ -> ...` for catch-all if needed.
- **Wrong casing**: types/constructors MUST start uppercase; values/functions MUST start lowercase.
- **Mixing spaces**: pick 2 or 4 spaces and be consistent throughout the file. Never use tabs.
- **Missing module header**: every file needs `module Path.Name` on line 1.

## 8. MCP Tools

ll-lang provides an MCP server (`lllc mcp`) with 10 tools. Use them for instant compiler feedback.

### Setup

```json
{
  "mcpServers": {
    "ll-lang": { "command": "lllc", "args": ["mcp"] }
  }
}
```

### Tool reference

| Tool | Use when | Input |
|------|----------|-------|
| `check_file` | Validate syntax + types (fast, no codegen) | `{ "path": "/path/to/file.lll" }` |
| `check_source` | Validate source string directly (fastest) | `{ "source": "module T\nadd(a Int)(b Int) = a + b" }` |
| `compile_file` | Full compile, optionally get generated output | `{ "path": "...", "include_output": true, "target": "fs" }` |
| `compile_source` | Compile source string, get generated code | `{ "source": "...", "target": "ts" }` |
| `run_file` | Compile and execute via dotnet fsi | `{ "path": "..." }` |
| `list_errors` | List all error codes | `{}` |
| `lookup_error` | Get explanation + repro for error code | `{ "code": "E003" }` |
| `stdlib_search` | Search stdlib by name or type signature | `{ "query": "list" }` |
| `grammar_lookup` | Get EBNF production for a grammar rule | `{ "rule": "Expr" }` |
| `project_info` | Get project metadata from ll.toml | `{ "path": "/path/to/src/Main.lll" }` |

### Recommended workflow

1. Write ll-lang code
2. Call `check_source` or `check_file` to validate
3. If errors, call `lookup_error` for details
4. If unsure about stdlib, call `stdlib_search`
5. If unsure about syntax, call `grammar_lookup`

Targets for `compile_file`/`compile_source`: `"fs"` (F#, default), `"ts"` (TypeScript), `"py"` (Python), `"java"` (Java/JVM).

## 9. Token Efficiency Tips

ll-lang is already designed for minimal tokens. To maximize compactness:

1. **Omit return types** -- HM inference handles it. Only annotate params.
2. **Use clause sugar** -- `f(x T) = | P1 -> ... | P2 -> ...` instead of `f(x T) = match x | ...`
3. **Use pipe** -- `x -> f -> g` instead of `g (f x)`
4. **Use bare declarations** -- no `fn`, no `type`, no `let` (except for simple constants)
5. **Single-line when possible** -- `add(a Int)(b Int) = a + b` (one line)
6. **Space-separated lists** -- `[1 2 3]` not `[1, 2, 3]` (commas form tuples)
7. **Wildcard patterns** -- `| _ ->` for unused bindings, `| Node _ left _ _ right ->` to skip fields
8. **Short names in local scope** -- `m`, `acc`, `xs` are idiomatic for local bindings
9. **Bind to `_` for effects** -- `_ = printfn "done"` is the minimal side-effect pattern
10. **Multi-line sum types** -- use indented `|` for readability without extra tokens

### Token comparison

```
-- ll-lang (18 tokens)
Maybe A = Some A | None
add(a Int)(b Int) = a + b

-- Equivalent Haskell (~25 tokens)
data Maybe a = Some a | None
add :: Int -> Int -> Int
add a b = a + b

-- Equivalent TypeScript (~35 tokens)
type Maybe<A> = { tag: "Some"; value: A } | { tag: "None" };
function add(a: number, b: number): number { return a + b; }
```

### Stdlib quick reference (most-used)

```
printfn     : Str -> Unit
listMap     : (A -> B) -> List[A] -> List[B]
listFold    : (B -> A -> B) -> B -> List[A] -> B
listFilter  : (A -> Bool) -> List[A] -> List[A]
listLen     : List[A] -> Int
listAppend  : List[A] -> List[A] -> List[A]
strConcat   : Str -> Str -> Str
strLen      : Str -> Int
strSplit    : Str -> Str -> List[Str]
strTrim     : Str -> Str
maybeMap    : (A -> B) -> Maybe[A] -> Maybe[B]
maybeDefault: A -> Maybe[A] -> A
resultMap   : (A -> B) -> Result[A,E] -> Result[B,E]
resultBind  : (A -> Result[B,E]) -> Result[A,E] -> Result[B,E]
```
