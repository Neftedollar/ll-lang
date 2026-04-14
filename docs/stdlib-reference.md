# Standard Library Reference

ll-lang ships two layers of stdlib:

1. **Prelude** — ~50 builtin functions always in scope. No `import` needed.
2. **Self-hosted modules** — modules written in ll-lang under `stdlib/src`. Import with `import Std.X`.

---

## Prelude (always in scope)

### Output

| Function | Type | Description |
|----------|------|-------------|
| `printfn` | `Str -> Unit` | Print string with newline |
| `print` | `Str -> Unit` | Print string without newline |

```lll
main() =
  _ = printfn "line one"
  _ = print "no newline"
  0
```

### List operations

| Function | Type | Description |
|----------|------|-------------|
| `listMap` | `(A -> B) -> List[A] -> List[B]` | Transform each element |
| `listFold` | `(B -> A -> B) -> B -> List[A] -> B` | Reduce left-to-right |
| `listFilter` | `(A -> Bool) -> List[A] -> List[A]` | Keep matching elements |
| `listLen` | `List[A] -> Int` | Number of elements |
| `listAppend` | `List[A] -> List[A] -> List[A]` | Concatenate two lists |
| `listAt` | `List[A] -> Int -> Maybe[A]` | Element at index (0-based) |
| `listHead` | `List[A] -> Maybe[A]` | First element |
| `listTail` | `List[A] -> Maybe[List[A]]` | All but first element |
| `listReverse` | `List[A] -> List[A]` | Reverse a list |
| `listConcat` | `List[List[A]] -> List[A]` | Flatten one level |
| `listRange` | `Int -> Int -> List[Int]` | `listRange 1 5` → `[1 2 3 4 5]` |
| `listSum` | `List[Int] -> Int` | Sum of integer list |
| `listJoin` | `Str -> List[Str] -> Str` | Join strings with separator |

```lll
-- double all even numbers
evensDoubled(xs List[Int]) =
  xs -> listFilter (\x. x == (x / 2 * 2)) -> listMap (\x. x * 2)

-- sum with fold
total = listFold (\acc x. acc + x) 0 [1 2 3 4 5]
```

### String operations

| Function | Type | Description |
|----------|------|-------------|
| `strConcat` | `Str -> Str -> Str` | Concatenate two strings |
| `strLen` | `Str -> Int` | Length in characters |
| `strSplit` | `Str -> Str -> List[Str]` | Split on separator |
| `strTrim` | `Str -> Str` | Strip leading/trailing whitespace |
| `strContains` | `Str -> Str -> Bool` | Substring test |
| `strStartsWith` | `Str -> Str -> Bool` | Prefix test |
| `strEndsWith` | `Str -> Str -> Bool` | Suffix test |
| `strSlice` | `Str -> Int -> Int -> Str` | Extract substring |
| `strReplace` | `Str -> Str -> Str -> Str` | Replace all occurrences |
| `strToUpper` | `Str -> Str` | Uppercase |
| `strToLower` | `Str -> Str` | Lowercase |
| `strToChars` | `Str -> List[Char]` | Explode to character list |
| `charsToStr` | `List[Char] -> Str` | Assemble from characters |
| `strToInt` | `Str -> Maybe[Int]` | Parse integer |
| `strToFloat` | `Str -> Maybe[Float]` | Parse float |

```lll
words = strSplit " " "hello world foo"  -- ["hello" "world" "foo"]
upper = strToUpper "hello"              -- "HELLO"
joined = listJoin ", " ["a" "b" "c"]   -- "a, b, c"
```

### Numeric conversions

| Function | Type | Description |
|----------|------|-------------|
| `intToStr` | `Int -> Str` | Integer to string |
| `floatToStr` | `Float -> Str` | Float to string |
| `intToFloat` | `Int -> Float` | Widen integer |
| `floatToInt` | `Float -> Int` | Truncate to integer |
| `abs` | `Int -> Int` | Absolute value |
| `absFloat` | `Float -> Float` | Absolute value (float) |
| `min` | `Int -> Int -> Int` | Minimum |
| `max` | `Int -> Int -> Int` | Maximum |

### Character operations

| Function | Type | Description |
|----------|------|-------------|
| `charToInt` | `Char -> Int` | Character code point |
| `intToChar` | `Int -> Char` | Code point to character |
| `charIsDigit` | `Char -> Bool` | `'0'`–`'9'` |
| `charIsAlpha` | `Char -> Bool` | `a`–`z`, `A`–`Z` |
| `charIsSpace` | `Char -> Bool` | Whitespace |
| `charIsUpper` | `Char -> Bool` | Uppercase letter |
| `charIsLower` | `Char -> Bool` | Lowercase letter |

### Maybe and Result

| Function | Type | Description |
|----------|------|-------------|
| `maybeMap` | `(A -> B) -> Maybe[A] -> Maybe[B]` | Transform the value if present |
| `maybeWithDefault` | `A -> Maybe[A] -> A` | Unwrap with a fallback |
| `maybeDefault` | `A -> Maybe[A] -> A` | Alias for `maybeWithDefault` |
| `maybeBind` | `(A -> Maybe[B]) -> Maybe[A] -> Maybe[B]` | Chain Maybe operations |
| `maybeToList` | `Maybe[A] -> List[A]` | `None` → `[]`, `Some x` → `[x]` |
| `resultMap` | `(A -> B) -> Result[A][E] -> Result[B][E]` | Transform Ok value |
| `resultBind` | `(A -> Result[B][E]) -> Result[A][E] -> Result[B][E]` | Chain Result operations |
| `resultMapErr` | `(E -> F) -> Result[A][E] -> Result[A][F]` | Transform Err value |
| `resultToMaybe` | `Result[A][E] -> Maybe[A]` | Discard error |
| `isOk` | `Result[A][E] -> Bool` | Test for Ok |
| `isErr` | `Result[A][E] -> Bool` | Test for Err |

```lll
-- chain operations that might fail
parseAndDouble(s Str) =
  s -> strToInt -> maybeMap (\n. n * 2)

-- unwrap with default
value = maybeWithDefault 0 (strToInt "42")  -- 42
missing = maybeWithDefault 0 (strToInt "x") -- 0
```

### File I/O

| Function | Type | Description |
|----------|------|-------------|
| `readFile` | `Str -> Str` | Read entire file as string |
| `writeFile` | `Str -> Str -> Unit` | Write string to file |
| `fileExists` | `Str -> Bool` | Test path existence |

### Process

| Function | Type | Description |
|----------|------|-------------|
| `getArgs` | `List[Str]` | Command-line arguments |
| `exit` | `Int -> Unit` | Exit with code |

---

## Category: Data Structures

### `Std.Map` — Ordered map (red-black tree)

**Import:** `import Std.Map`  
**LOC:** 223  
**Description:** Functional ordered map using Okasaki's red-black tree. O(log n) insert, lookup, and delete. Keys are compared via an explicit comparator function, making it usable with any ordered type.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `mapEmpty` | `RBMap[K][V]` | Empty map value |
| `mapInsert` | `(K -> K -> Int) -> K -> V -> RBMap[K][V] -> RBMap[K][V]` | Insert key-value pair |
| `mapLookup` | `(K -> K -> Int) -> K -> RBMap[K][V] -> Maybe[V]` | Lookup by key |
| `mapDelete` | `(K -> K -> Int) -> K -> RBMap[K][V] -> RBMap[K][V]` | Remove key |
| `mapSize` | `RBMap[K][V] -> Int` | Number of entries |
| `mapFold` | `(B -> K -> V -> B) -> B -> RBMap[K][V] -> B` | In-order fold |
| `mapKeys` | `RBMap[K][V] -> List[K]` | All keys in sorted order |
| `mapValues` | `RBMap[K][V] -> List[V]` | All values in key order |
| `mapToList` | `RBMap[K][V] -> List[(K, V)]` | All pairs in sorted order |

**Usage:**

```lll
module MyApp.Example

import Std.Map

-- String comparator (lexicographic)
strCmp(a Str)(b Str) =
  if a < b then -1
  else if a > b then 1
  else 0

-- Build a map
scores =
  mapEmpty
    -> mapInsert strCmp "alice" 95
    -> mapInsert strCmp "bob" 87
    -> mapInsert strCmp "carol" 92

-- Lookup
aliceScore = mapLookup strCmp "alice" scores  -- Some 95
missing    = mapLookup strCmp "dave"  scores  -- None

-- Iterate
total = mapFold (\acc _k v. acc + v) 0 scores  -- 274
```

---

## Category: Parsing and Config

### `Std.Toml` — TOML config parser

**Import:** `import Std.Toml`  
**LOC:** 292  
**Description:** Minimal TOML parser for project manifests. Parses `[table]` headers, `key = "value"` string pairs, and `key = ["a", "b"]` string arrays. Pure functional, no mutable state.

**Types:**

```lll
Manifest = MkManifest Str Str Str (List[Str]) (List[Str])
--                    name version entry deps    platform
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `parseManifest` | `Str -> Maybe[Manifest]` | Parse TOML string to manifest |
| `manifestName` | `Manifest -> Str` | Project name |
| `manifestVersion` | `Manifest -> Str` | Project version |
| `manifestDeps` | `Manifest -> List[Str]` | Dependency keys (flat list) |
| `manifestPlatform` | `Manifest -> List[Str]` | Platform target names |

**Usage:**

```lll
module MyApp.Config

import Std.Toml

loadConfig(path Str) =
  content = readFile path
  match parseManifest content
    | None -> printfn "invalid config"
    | Some m -> printfn (manifestName m)
```

---

### `Std.Json` — JSON parser and serializer

**Import:** `import Std.Json`  
**LOC:** ~420  
**Description:** JSON parser and serializer built on `Std.Parsec` combinators. Supports strict number shape validation, string escapes (`\uXXXX` + surrogate pairs), deterministic serializer, and structural equality helpers.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `parseJson` | `Str -> ParseResult[JsonValue]` | Parse JSON text into AST |
| `stringify` | `JsonValue -> Str` | Serialize AST back to JSON text |
| `equalJson` | `JsonValue -> JsonValue -> Bool` | Structural AST equality |

Backward-compatible aliases are also provided:
- `parse` (alias of `parseJson`)
- `renderJson` (alias-compatible implementation for `stringify`)
- `eqJsonValue` (alias-compatible implementation for `equalJson`)

**Usage:**

```lll
module MyApp.JsonUse

import Std.Json

validateAndRoundtrip(src Str) =
  match parseJson src
    | ParseErr e -> strConcat "ERR: " e
    | ParseOk v _ ->
      out = stringify v
      match parseJson out
        | ParseErr e2 -> strConcat "ERR2: " e2
        | ParseOk v2 _ ->
          if equalJson v v2
            "OK"
          else "MISMATCH"
```

---

### `Std.State` — stateful computation primitives

**Import:** `import Std.State`  
**Description:** Concrete state monad foundation for self-hosted compiler passes and imperative-style pipelines via `stateBind`.

**Key types:**

```lll
StateUnit = StateUnit
StatePair A S = MkStatePair A S
State S A = MkState (S -> StatePair[A][S])
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `stateRun` | `State[S][A] -> S -> StatePair[A][S]` | Run stateful computation |
| `stateEval` | `State[S][A] -> S -> A` | Extract result value |
| `stateExec` | `State[S][A] -> S -> S` | Extract final state |
| `statePure` | `A -> State[S][A]` | Lift pure value |
| `stateMap` | `(A -> B) -> State[S][A] -> State[S][B]` | Map over result |
| `stateBind` | `State[S][A] -> (A -> State[S][B]) -> State[S][B]` | Sequence computations |
| `stateGet` | `S -> State[S][S]` | Read state (seed argument pins `S` in current compiler) |
| `statePut` | `S -> State[S][StateUnit]` | Replace state |
| `stateModify` | `(S -> S) -> State[S][StateUnit]` | Transform state |

`stateGet` is temporarily argument-based due current top-level polymorphic value inference limits; once zero-arg polymorphic values are stabilized it can be reduced to a plain `State[S][S]` value.

---

### `Std.Parsec` — parser combinator toolkit

**Import:** `import Std.Parsec`  
**Description:** Reusable parser-combinator substrate for self-hosted parsing tasks. Works over source text with explicit position tracking and backtracking control.

**Key types:**

```lll
ParsePos = MkParsePos Int Int Int
ParseState = MkParseState Str List[Char] ParsePos
ParseError = MkParseError Str ParsePos
Parser A = MkParser (ParseState -> ParseStep[A])
```

**Core functions:**

| Function | Description |
|----------|-------------|
| `runParser` | Run parser on `Str` and return `Result` |
| `parsePure` / `parseMap` / `parseBind` | Core combinators |
| `parseOrElse` / `parseTry` | Choice + controlled rollback |
| `parseLabel` / `parseFail` | Diagnostics helpers |
| `parseSatisfy` / `parseChar` / `parseString` | Primitive token parsers |
| `parseOneOf` / `parseNoneOf` / `parsePeekChar` / `parseAnyChar` / `parseEof` | Character/EOF parsers |
| `parseMany` / `parseMany1` / `parseOptional` / `parseSepBy` / `parseSepBy1` / `parseBetween` | Structural combinators |
| `parseWhitespace` / `parseSpaces` / `parseDigit` / `parseInt` / `parseQuotedString` | Common building blocks |

`Std.Json` uses `Std.Parsec` as its parser backend.

Combinator pipelines can use either parenthesized lambdas or trailing lambda sugar:

```lll
parseBind parseInt \n.
  parsePure (n + 1)
```

---

### `Std.Lazy` — explicit laziness on top of strict evaluation

**Import:** `import Std.Lazy`  
**Description:** Controlled delayed evaluation for expensive or recursive computations without changing strict language semantics.

**Key types:**

```lll
Lazy[A] = Delayed (Int -> A) | Ready A
```

**Core functions:**

| Function | Type | Description |
|----------|------|-------------|
| `lazyDelay` | `(Int -> A) -> Lazy[A]` | Create delayed value |
| `lazyReady` | `A -> Lazy[A]` | Create already-forced value |
| `lazyForce` | `Lazy[A] -> (A, Lazy[A])` | Force and return memoized node |
| `lazyValue` | `Lazy[A] -> A` | Force and return value only |
| `lazyMap` | `(A -> B) -> Lazy[A] -> Lazy[B]` | Map through delayed value |
| `lazyBind` | `Lazy[A] -> (A -> Lazy[B]) -> Lazy[B]` | Compose delayed computations |

---

### `Std.Lexer` — ll-lang tokenizer

**Import:** `import Std.Lexer`  
**LOC:** 473  
**Description:** Standalone ll-lang lexer written in ll-lang. Tokenizes ll-lang source to a flat `List[Token]`. Recognizes all language keywords, identifiers (by case), literals, and operators.

**Token type (selected constructors):**

```lll
Token =
  | KwIf | KwElse | KwMatch | KwTrait | KwImpl
  | KwImport | KwExport | KwModule | KwTag | KwUnit
  | Ident Str      -- lowercase identifier
  | TypeId Str     -- uppercase identifier / constructor
  | IntLit Int     | FloatLit Str | StrLit Str | CharLit Char
  | Arrow | Eq | Bar | LBrack | RBrack | LParen | RParen
  | Plus | Minus | Star | Slash
  | Newline | Indent | Dedent | Eof
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `tokenize` | `Str -> List[Token]` | Lex a source string to token list |
| `tokenToStr` | `Token -> Str` | Debug representation of a token |
| `isKeyword` | `Token -> Bool` | Test for keyword token |

**Usage:**

```lll
module MyApp.Analyze

import Std.Lexer

countIdents(src Str) =
  tokens = tokenize src
  listLen (listFilter (\t. match t | Ident _ -> true | _ -> false) tokens)
```

---

### `Std.Parser` — ll-lang recursive-descent parser

**Import:** `import Std.Parser`  
**LOC:** 802  
**Description:** Recursive-descent parser consuming a `List[Token]` from `Std.Lexer` and producing an AST. Return convention: every parse function returns `(result, remainingTokens)`.

**Key types:**

```lll
Expr =
  | EInt Int | EStr Str | EBool Bool | EVar Str | ECon Str
  | EApp Expr Expr | ELam Str Expr | EIf Expr Expr Expr
  | EMatch Expr (List[Pattern]) (List[Expr])
  | ELet Str Expr Expr | EBinOp Str Expr Expr

Decl =
  | DFn Str (List[(Str, TypeExpr)]) (Maybe[TypeExpr]) Expr
  | DType Str (List[Str]) (List[(Str, List[TypeExpr])])
  | DTag Str | DImport Str | DExport Decl
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `parseModule` | `List[Token] -> Maybe[LLModule]` | Parse full module from tokens |
| `parseExpr` | `List[Token] -> Maybe[(Expr, List[Token])]` | Parse a single expression |
| `parseDecl` | `List[Token] -> Maybe[(Decl, List[Token])]` | Parse a single declaration |

**Usage:**

```lll
module MyApp.Parse

import Std.Lexer
import Std.Parser

parseSource(src Str) =
  tokens = tokenize src
  match parseModule tokens
    | None -> printfn "parse error"
    | Some m -> printfn "ok"
```

---

## Category: Code Generation

### `Std.Elaborator` — name resolver / type checker

**Import:** `import Std.Elaborator`  
**LOC:** 344  
**Description:** Simplified elaborator pass operating on the AST from `Std.Parser`. Performs name resolution: detects unbound variables, unbound constructors, and duplicate declarations. Produces an enriched AST or a list of error messages.

**Key types:**

```lll
ElabError =
  | UnboundVar Str
  | UnboundCon Str
  | DuplicateDecl Str

ElabResult A = ElabOk A | ElabErr (List[ElabError])
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `elaborate` | `LLModule -> ElabResult[LLModule]` | Check a module for binding errors |
| `collectDecls` | `LLModule -> Env` | Build environment from module decls |
| `checkExpr` | `Env -> Expr -> List[ElabError]` | Check an expression |

---

### `Std.Codegen` — F# code emitter

**Import:** `import Std.Codegen`  
**LOC:** 569  
**Description:** F# source emitter written in ll-lang. Takes the AST from `Std.Parser` and emits idiomatic F# source. This is the self-hosted reference implementation of the F# backend.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `emitModule` | `LLModule -> Str` | Emit entire module as F# source |
| `emitDecl` | `Decl -> Str` | Emit one declaration |
| `emitExpr` | `Expr -> Int -> Str` | Emit expression (indent level) |
| `emitType` | `TypeExpr -> Str` | Emit type annotation |

**Usage:**

```lll
module MyApp.Transpile

import Std.Lexer
import Std.Parser
import Std.Codegen

transpileToFSharp(src Str) =
  tokens = tokenize src
  match parseModule tokens
    | None -> Err "parse error"
    | Some m -> Ok (emitModule m)
```

---

### `Std.CodegenTS` — TypeScript emitter

**Import:** `import Std.CodegenTS`  
**LOC:** 492  
**Description:** TypeScript source emitter written in ll-lang. Emits TypeScript sealed interfaces for ADTs, `readonly` records, and typed functions.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `emitModule` | `LLModule -> Str` | Emit module as TypeScript source |
| `emitDecl` | `Decl -> Str` | Emit one declaration |
| `emitType` | `TypeExpr -> Str` | Emit TypeScript type annotation |

---

### `Std.CodegenPy` — Python emitter

**Import:** `import Std.CodegenPy`  
**LOC:** 501  
**Description:** Python source emitter. Emits `@dataclass` classes, `Union` types from `typing`, and module-level functions.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `emitModule` | `LLModule -> Str` | Emit module as Python source |
| `emitDecl` | `Decl -> Str` | Emit one declaration |
| `emitType` | `TypeExpr -> Str` | Emit Python type annotation |

---

### `Std.CodegenJava` — Java 21 emitter

**Import:** `import Std.CodegenJava`  
**LOC:** 633  
**Description:** Java 21 source emitter. Emits `sealed interface` + `record` hierarchies for ADTs, static methods for functions.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `emitModule` | `LLModule -> Str` | Emit module as Java source |
| `emitDecl` | `Decl -> Str` | Emit one declaration |
| `emitType` | `TypeExpr -> Str` | Emit Java type annotation |

---

### `Std.Compiler` — full pipeline (source → F#)

**Import:** `import Std.Compiler`  
**LOC:** 1516  
**Description:** End-to-end compiler pipeline in ll-lang: Lexer → Parser → Elaborator → Codegen. Self-contained — all types defined inline. This is the bootstrap compiler that produced `compiler₁.fs == compiler₂.fs`.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `compile` | `Str -> Result[Str][List[Str]]` | Compile source string to F# |
| `compileFile` | `Str -> Result[Str][List[Str]]` | Read file, compile, return F# |
| `pipeline` | `Str -> Maybe[LLModule]` | Lex + parse only |

**Usage:**

```lll
module MyApp.Build

import Std.Compiler

buildFile(path Str) =
  src = readFile path
  match compile src
    | Err errs -> listFold (\_ e. printfn e) () errs
    | Ok fsharp -> writeFile (strConcat path ".fs") fsharp
```

---

## Module index by category

| Category | Module | Purpose |
|----------|--------|---------|
| Core types | `Std.Maybe` | Maybe helpers and tests |
| Data structures | `Std.Map` | Ordered map, O(log n) |
| Config | `Std.Toml` | TOML manifest parser |
| Parsing | `Std.Json` | JSON parse + stringify + roundtrip helpers |
| Parsing | `Std.Parsec` | Parser combinator substrate |
| Runtime | `Std.Lazy` | Explicit delayed evaluation |
| Parsing | `Std.Lexer` | ll-lang tokenizer |
| Parsing | `Std.Parser` | Recursive-descent parser |
| Type checking | `Std.Elaborator` | Name resolution, binding checks |
| Code generation | `Std.Codegen` | F# emitter |
| Code generation | `Std.CodegenTS` | TypeScript emitter |
| Code generation | `Std.CodegenPy` | Python emitter |
| Code generation | `Std.CodegenJava` | Java emitter |
| Code generation | `Std.CodegenLLVM` | LLVM emitter |
| Rendering | `Std.Render` | Shared rendering helpers |
| Testing | `Std.Test` | Test assertions/utilities |
| Full pipeline | `Std.Compiler` | Source pipeline helpers |

---

## Notes on self-hosted modules

The stdlib modules are written in ll-lang and compiled via the bootstrap compiler. Library modules are no longer expected to be independently runnable via `main()`; smoke/demo entrypoints should live in dedicated executable modules.

When using multiple modules together in a project, import them in dependency order: `Std.Lexer` before `Std.Parser` before `Std.Elaborator` before `Std.Codegen`.
