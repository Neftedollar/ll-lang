# Standard Library Reference

ll-lang ships two layers of stdlib:

1. **Prelude** — ~50 builtin functions always in scope. No `import` needed.
2. **Self-hosted modules** — modules written in ll-lang under `stdlib/src`. Import with `import Std.X`.

## Module groups (frozen for v2)

The self-hosted modules fall into two **distinct, non-overlapping groups**:

### Group 1 — Reusable foundation stdlib (`Std.*`)

These are general-purpose library modules. Import them in any ll-lang program.

| Module | Import | Purpose |
|--------|--------|---------|
| `Std.List` | `import Std.List` | list operations |
| `Std.Maybe` | `import Std.Maybe` | optional values |
| `Std.Result` | `import Std.Result` | error-or-value |
| `Std.Map` | `import Std.Map` | Okasaki RB-tree map |
| `Std.Str` | `import Std.Str` | string utilities |
| `Std.State` | `import Std.State` | stateful computation |
| `Std.Parsec` | `import Std.Parsec` | parser combinators |
| `Std.Lazy` | `import Std.Lazy` | explicit laziness |
| `Std.Json` | `import Std.Json` | JSON codec |
| `Std.Toml` | `import Std.Toml` | TOML subset parser |
| `Std.Test` | `import Std.Test` | unit test harness |

### Group 2 — Compiler implementation (`Compiler.*`)

These modules implement the self-hosted compiler. They live under `stdlib/src/` today but are
**not general-purpose stdlib**. Their canonical namespace is `Compiler.*` (see
[14-v2-canonical-compiler-boundaries.md](compiler-dev/14-v2-canonical-compiler-boundaries.md)).

| Current file | Canonical v2 module | Role |
|-------------|--------------------|----|
| `stdlib/src/Lexer.lll` | `Compiler.Syntax.Lexer` | tokenizer |
| `stdlib/src/Parser.lll` | `Compiler.Syntax.Parser` | recursive-descent parser |
| `stdlib/src/Elaborator.lll` | `Compiler.Frontend.Elaborator` | name resolution + declared-type checks |
| `stdlib/src/Codegen.lll` | `Compiler.Backend.FSharp` | F# emitter |
| `stdlib/src/CodegenTS.lll` | `Compiler.Backend.TypeScript` | TypeScript emitter |
| `stdlib/src/CodegenPy.lll` | `Compiler.Backend.Python` | Python emitter |
| `stdlib/src/CodegenJava.lll` | `Compiler.Backend.Java` | Java emitter |
| `stdlib/src/CodegenLLVM.lll` | `Compiler.Backend.LLVM` | LLVM IR emitter (experimental) |
| `stdlib/src/CodegenCSharp.lll` | `Compiler.Backend.CSharp` | C# emitter |
| `stdlib/src/Compiler.lll` | `Compiler.Main` | pipeline entrypoint |
| `stdlib/src/Render.lll` | `Compiler.Render` | diagnostic/output rendering |

**Do not `import` compiler implementation modules from user programs.** They are internal to the
compiler toolchain.

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

## Category: Core Types

### `Std.Maybe` — Optional values

**Import:** `import Std.Maybe`  
**Self-tests:** 6/6 (isSome/isNone/pattern-match tests; maybeMap/maybeBind tested via F# backend target only — C# prelude type-inference limitation)  
**Description:** Canonical optional type with predicate helpers. Core `maybeMap`/`maybeBind`/`maybeWithDefault` are also available as prelude builtins without an import.

**Type:**

```lll
Maybe A = Some A | None
```

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `isSome` | `Maybe[A] -> Bool` | True if Some |
| `isNone` | `Maybe[A] -> Bool` | True if None |

---

### `Std.Result` — Error-or-value

**Import:** `import Std.Result`  
**Self-tests:** 20/20  
**Description:** Canonical Result type for recoverable errors. Never throw; propagate with `resultBind`. Integrates with `Std.Maybe` via `resultFromMaybe`/`resultToMaybe`.

**Type:**

```lll
Result A E = Ok A | Err E
```

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `resultWithDefault` | `A -> Result[A][E] -> A` | Unwrap Ok or return default |
| `resultMap` | `(A -> B) -> Result[A][E] -> Result[B][E]` | Transform Ok value |
| `resultMapErr` | `(E -> F) -> Result[A][E] -> Result[A][F]` | Transform Err value |
| `resultBind` | `Result[A][E] -> (A -> Result[B][E]) -> Result[B][E]` | Chain fallible computations |
| `resultFold` | `(A -> C) -> (E -> C) -> Result[A][E] -> C` | Fold over both branches |
| `resultOrElse` | `(E -> Result[A][E]) -> Result[A][E] -> Result[A][E]` | Recover from Err |
| `resultIsOk` | `Result[A][E] -> Bool` | Test for Ok |
| `resultIsErr` | `Result[A][E] -> Bool` | Test for Err |
| `resultFromMaybe` | `E -> Maybe[A] -> Result[A][E]` | Lift Maybe; use errValue on None |
| `resultToMaybe` | `Result[A][E] -> Maybe[A]` | Discard error |
| `resultSequence` | `List[Result[A][E]] -> Result[List[A]][E]` | Collect Ok or short-circuit |
| `resultTraverse` | `(A -> Result[B][E]) -> List[A] -> Result[List[B]][E]` | Map+sequence |

**Usage:**

```lll
import Std.Result

failIfZero(n Int) =
  if n == 0
    Err "zero"
  else Ok n

pipeline(s Str) =
  resultBind (resultFromMaybe "not a number" (strToInt s)) failIfZero
```

---

### `Std.List` — Extended list operations

**Import:** `import Std.List`  
**Self-tests:** 16/16  
**Description:** Extended list operations beyond the prelude. Import adds `listTake`, `listDrop`, `listFlatMap`, `listAny`, `listAll`, `listFind`, `listFindIndex`, and `listPartition`.

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `listTake` | `Int -> List[A] -> List[A]` | First N elements |
| `listDrop` | `Int -> List[A] -> List[A]` | Skip first N elements |
| `listFlatMap` | `(A -> List[B]) -> List[A] -> List[B]` | Map then flatten |
| `listAny` | `(A -> Bool) -> List[A] -> Bool` | Any element satisfies predicate |
| `listAll` | `(A -> Bool) -> List[A] -> Bool` | All elements satisfy predicate |
| `listFind` | `(A -> Bool) -> List[A] -> Maybe[A]` | First matching element |
| `listFindIndex` | `(A -> Bool) -> List[A] -> Maybe[Int]` | Index of first match |
| `listPartition` | `(A -> Bool) -> List[A] -> (List[A], List[A])` | Split into passing/failing |

---

### `Std.Str` — Extended string operations

**Import:** `import Std.Str`  
**Self-tests:** 14/14  
**Description:** String utility functions beyond the prelude. Adds `isEmpty`, `startsWith`/`endsWith`, `take`/`drop`, `repeat`, `padLeft`/`padRight`.

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `strIsEmpty` | `Str -> Bool` | True if length is 0 |
| `strStartsWith` | `Str -> Str -> Bool` | Prefix test |
| `strEndsWith` | `Str -> Str -> Bool` | Suffix test |
| `strTake` | `Int -> Str -> Str` | First N characters |
| `strDrop` | `Int -> Str -> Str` | Skip first N characters |
| `strRepeat` | `Int -> Str -> Str` | Repeat string N times |
| `strPadLeft` | `Int -> Str -> Str -> Str` | Left-pad to width with fill |
| `strPadRight` | `Int -> Str -> Str -> Str` | Right-pad to width with fill |

---

### `Std.Lazy` — Explicit laziness

**Import:** `import Std.Lazy`  
**Self-tests:** 6/6  
**Description:** Controlled deferred evaluation for strict language semantics. Models thunks as `Int -> A` to avoid a dedicated Unit type.

**Type:**

```lll
Lazy A = Delayed (Int -> A) | Ready A
```

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `lazyDelay` | `(Int -> A) -> Lazy[A]` | Create deferred value |
| `lazyReady` | `A -> Lazy[A]` | Already-evaluated value |
| `lazyForce` | `Lazy[A] -> (A, Lazy[A])` | Force + memoize |
| `lazyValue` | `Lazy[A] -> A` | Force and extract value |
| `lazyMap` | `(A -> B) -> Lazy[A] -> Lazy[B]` | Map through deferred value |
| `lazyBind` | `Lazy[A] -> (A -> Lazy[B]) -> Lazy[B]` | Compose deferred computations |

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
**Self-tests:** 5/5  
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
**Self-tests:** 12/12  
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
**Self-tests:** 7/7  
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

### `Std.Test` — unit test harness

**Import:** `import Std.Test`  
**Self-tests:** 8 (4 pass + 4 intentional fails exercising the harness itself)  
**Description:** Lightweight test harness for ll-lang modules. Collects `TestResult` values, prints OK/FAIL lines, and returns a failure count. Intended for module self-tests reachable via `lllc run`.

**Type:**

```lll
TestResult = Pass Str | Fail Str
```

**Functions:**

| Function | Type | Description |
|----------|------|-------------|
| `assertTrue` | `Str -> Bool -> TestResult` | Pass iff condition is true |
| `assertEqInt` | `Str -> Int -> Int -> TestResult` | Pass iff ints match |
| `assertEqStr` | `Str -> Str -> Str -> TestResult` | Pass iff strings match |
| `assertEqBool` | `Str -> Bool -> Bool -> TestResult` | Pass iff bools match |
| `renderResult` | `TestResult -> Str` | Format OK/FAIL line |
| `run` | `List[TestResult] -> Int` | Print all results; return fail count |

**Usage:**

```lll
module MyModule

import Std.Test

main =
  failCount = run
    [ assertEqInt "add" 5 (2 + 3)
    ; assertEqStr "hello" "hello" "hello"
    ; assertTrue "positive" (3 > 0)
    ]
  printfn (strConcat "Done failCount=" (intToStr failCount))
```

---

### `Std.Parsec` — parser combinator toolkit

**Import:** `import Std.Parsec`  
**Self-tests:** 12/12  
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

### `Std.CodegenCSharp` — C# emitter

**Import:** `import Std.CodegenCSharp`  
**LOC:** 548  
**Description:** C# source emitter. Emits `sealed record`/`interface` hierarchies for ADTs, static methods for functions, wrapped IIFE style for `let`-bindings.

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `emitModule` | `LLModule -> Str` | Emit module as C# source |
| `emitDecl` | `Decl -> Str` | Emit one declaration |
| `emitType` | `TypeExpr -> Str` | Emit C# type annotation |

---

### `Std.CompilerTypes` — HM type representations

**Import:** `import Std.CompilerTypes`  
**LOC:** 336  
**Description:** Hindley-Milner type representations for the v2 self-hosted compiler. Provides `Scheme`, substitution helpers (`RBMap[Str][TypeExpr]`), free-type-variable utilities, fresh variable generation, unification (`unify`), and `generalize`/`instantiate`. Canonical home for all type-level machinery shared across compiler passes.

**Key types:**

```lll
-- Flex (unification) vars: TyName "$0", TyName "$1", ...
-- Rigid type vars: TyName "a", TyName "b", ...
-- Type names: TyName "Int", TyName "Maybe", ...

Scheme = MkScheme (List[Str]) TypeExpr

InferResult A = InferOk A | InferErr Str

Fresh = MkFresh Int
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `isFlex` | `Str -> Bool` | True if name starts with `$` |
| `freshVarName` | `Int -> Str` | `"$N"` |
| `freshNext` | `Fresh -> (TypeExpr, Fresh)` | Allocate next flex var |
| `generalize` | `RBMap[Str][Scheme] -> TypeExpr -> Scheme` | Generalize over free flex vars |
| `instantiate` | `Fresh -> Scheme -> (TypeExpr, Fresh)` | Replace quantified vars with fresh flex vars |
| `unify` | `TypeExpr -> TypeExpr -> InferResult[RBMap[Str][TypeExpr]]` | Algorithm W unification with occurs check |
| `applyType` | `RBMap[Str][TypeExpr] -> TypeExpr -> TypeExpr` | Apply substitution to a type |
| `substCompose` | `Subst -> Subst -> Subst` | Compose substitutions (s1 after s2) |
| `renderType` | `TypeExpr -> Str` | Pretty-print a type for error messages |
| `ftvTypeList` | `TypeExpr -> List[Str]` | Free flex vars in a type |

---

### `Std.CompilerTyped` — typed IR shapes

**Import:** `import Std.CompilerTyped`  
**LOC:** 206  
**Description:** Typed intermediate representation (IR) for the v2 self-hosted compiler. Defines `TypedExpr`, `TypedPattern`, `TypedFnSig`, `TypedDecl`, and `TypedModule`. These are the output shapes produced by `Std.CompilerInfer` and consumed by lowering and backend passes. Flat sum-type design avoids mutual-recursion constraints.

**Key types:**

```lll
-- Pattern with resolved type
TypedPattern = MkTypedPat Pattern TypeExpr

-- Expression nodes (last arg is always the expression's TypeExpr)
TypedExpr =
  | TEInt Int TypeExpr
  | TEStr Str TypeExpr
  | TEBool Bool TypeExpr
  | TEVar Str TypeExpr
  | TECon Str TypeExpr
  | TEApp TypedExpr TypedExpr TypeExpr
  | TELam Str TypeExpr TypedExpr TypeExpr
  | TeLet Str TypedExpr TypedExpr TypeExpr
  | TEIf TypedExpr TypedExpr TypedExpr TypeExpr
  | TEMatch TypedExpr (List[TypedPattern]) (List[TypedExpr]) TypeExpr
  | TEBinOp Str TypedExpr TypedExpr TypeExpr
  | ...

TypedDecl =
  | TDFn TypedFnSig Scheme TypedExpr
  | TDLet Str Scheme TypedExpr
  | TDType Str (List[Str]) (List[Constructor])
  | ...

TypedModule = MkTypedModule (List[Str]) (List[TypedDecl]) (RBMap[Str][Scheme])
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `typedExprType` | `TypedExpr -> TypeExpr` | Extract type from any node |
| `typedPatType` | `TypedPattern -> TypeExpr` | Extract type from pattern |
| `typedFnName` | `TypedFnSig -> Str` | Function name |
| `typedModDecls` | `TypedModule -> List[TypedDecl]` | Module declarations |
| `renderTypedExprTag` | `TypedExpr -> Str` | Tag name for debugging |

---

### `Std.CompilerInfer` — HM Algorithm W inference

**Import:** `import Std.CompilerInfer`  
**LOC:** 471  
**Description:** Hindley-Milner Algorithm W type inference for the v2 self-hosted compiler. Builds on `Std.CompilerTypes`. Provides `InferState` threading, a prelude base environment, and `inferExpr` / `inferDecl` / `inferModule` for inferring types over the `Expr`/`Decl` AST from `Std.Parser`.

**Key types:**

```lll
-- (env, fresh counter, error list, accumulated substitution)
InferState = MkInferState
  (RBMap[Str][Scheme])      -- type environment
  Fresh                      -- fresh variable counter
  (List[Str])                -- accumulated errors
  (RBMap[Str][TypeExpr])     -- accumulated substitution
```

**Key functions:**

| Function | Type | Description |
|----------|------|-------------|
| `inferExpr` | `InferState -> Expr -> (TypeExpr, InferState)` | Infer type of an expression |
| `inferDecl` | `InferState -> Decl -> InferState` | Elaborate one declaration into the env |
| `inferModule` | `List[Decl] -> List[Str]` | Infer a whole module; return errors |
| `baseEnv` | `() -> RBMap[Str][Scheme]` | Prelude: arithmetic, comparison, `printfn`, etc. |
| `inferUnify` | `InferState -> TypeExpr -> TypeExpr -> Str -> InferState` | Unify two types, compose substitution |
| `inferFreshVar` | `InferState -> (TypeExpr, InferState)` | Allocate a fresh flex var |
| `inferInstantiate` | `InferState -> Scheme -> (TypeExpr, InferState)` | Instantiate a scheme |

**Usage:**

```lll
module MyApp.Check

import Std.Parser
import Std.CompilerInfer

checkSource(src Str) =
  tokens = tokenize src
  match parseModule tokens
    | None -> ["parse error"]
    | Some m ->
      inferModule m.decls
```

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
| Code generation | `Std.CodegenCSharp` | C# emitter |
| Rendering | `Std.Render` | Shared rendering helpers |
| Testing | `Std.Test` | Test assertions/utilities |
| Type inference | `Std.CompilerTypes` | HM substitutions, unification, schemes |
| Type inference | `Std.CompilerTyped` | Typed IR shapes (TypedExpr, TypedDecl, TypedModule) |
| Type inference | `Std.CompilerInfer` | Algorithm W for Expr/Decl |
| Full pipeline | `Std.Compiler` | Source pipeline helpers |

---

## Compiler implementation modules vs reusable stdlib

The modules below are currently importable like ordinary stdlib modules, but
they should be read as the self-hosted compiler slice rather than as general
application-library APIs:

- `Std.Lexer`
- `Std.Parser`
- `Std.Elaborator`
- `Std.CompilerTypes`
- `Std.CompilerTyped`
- `Std.CompilerInfer`
- `Std.Codegen`
- `Std.CodegenTS`
- `Std.CodegenPy`
- `Std.CodegenJava`
- `Std.CodegenLLVM`
- `Std.Compiler`

These modules are important because they show that ll-lang can already express
substantial compiler logic. They should not be treated as proof that the full
canonical compiler boundary has already migrated away from stage0. That
boundary is tracked in
[v2 canonical compiler boundaries](/Users/roman/Documents/dev/tens/code/ll-lang/docs/compiler-dev/14-v2-canonical-compiler-boundaries.md).

---

## Notes on self-hosted modules

The stdlib modules are written in ll-lang and compiled via the bootstrap compiler. Library modules are no longer expected to be independently runnable via `main()`; smoke/demo entrypoints should live in dedicated executable modules.

When using multiple modules together in a project, import them in dependency order: `Std.Lexer` before `Std.Parser` before `Std.Elaborator` before `Std.Codegen`.

---

## Naming convention (frozen for v2)

All public stdlib names use a **`verbOnType` prefix style** — no namespacing inside modules, no operator overloading. The type prefix is lowercase and comes first.

| Type | Prefix | Example |
|------|--------|---------|
| `List` | `list` | `listMap`, `listFilter`, `listFold` |
| `Maybe` | `maybe` | `maybeMap`, `maybeBind`, `maybeWithDefault` |
| `Result` | `result` | `resultMap`, `resultBind`, `resultIsOk` |
| `Map` | `map` | `mapInsert`, `mapLookup`, `mapDelete` |
| `Str` | `str` | `strConcat`, `strSplit`, `strTrim` |
| `State` | `state` | `stateRun`, `stateBind`, `stateGet` |
| `Parsec` | `parse` | `parsePure`, `parseBind`, `parseMany` |
| `Lazy` | `lazy` | `lazyDelay`, `lazyForce`, `lazyValue` |
| `Json` | `json` | `jsonParse`, `jsonStringify` |
| `Toml` | `toml` | `tomlParse` |

**Rules:**
1. The type-name prefix is always lowercase and always first in the name.
2. No two functions in different modules may share an unambiguous short name (e.g., `map` is not a valid export — use `mapInsert`).
3. Prelude function names are owned by the prelude; imported modules must not re-export conflicting names.
4. Legacy names or aliases are removed aggressively; backwards-compatibility shims are not kept.

---

## Prelude boundary rule (frozen for v2)

**A function belongs in the Prelude if and only if:**
- It is needed by almost every ll-lang program (≥ 90% of programs would import it otherwise)
- It operates on a type that is built into the language (`List`, `Maybe`, `Str`, `Int`, `Bool`, `Char`)
- Its definition is 1–3 lines and has no non-trivial dependencies

**A function does NOT belong in the Prelude if:**
- It is useful only in specific domains (parsing, state threading, config loading)
- It requires importing another stdlib module to typecheck
- It could be replaced by a 2–3 line inline in the call site

**Current Prelude surface is frozen.** Adding to it requires removing something of equal or
greater cognitive weight. The prelude must stay small enough that a developer can memorize
it in one sitting.

---

## Compiler-facing proof-of-use (M3.H)

The following sketch shows the foundation modules supporting a real compiler-shaped task:
a single-pass diagnostic accumulator that resolves imports, collects errors, and formats them.

This uses `Std.State` for pass state, `Std.Maybe` for optional lookups, `Std.Map` for the
module registry, and `Std.Result` for error propagation.

```lll
module Compiler.ImportResolver

import Std.State
import Std.Maybe
import Std.Map
import Std.Result

-- Pass state: seen module names + accumulated diagnostics
ResolverState = MkResolverState
  (RBMap[Str][Bool])   -- seen modules
  (List[Str])          -- diagnostics

emptyResolverState() =
  MkResolverState (mapEmpty()) []

addDiag(diag Str)(s ResolverState) =
  match s
    | MkResolverState seen diags ->
      MkResolverState seen (diag :: diags)

markSeen(modName Str)(s ResolverState) =
  match s
    | MkResolverState seen diags ->
      MkResolverState (mapInsert strCmp modName true seen) diags

isSeen(modName Str)(s ResolverState) =
  match s
    | MkResolverState seen _ -> isSome (mapLookup strCmp modName seen)

-- Resolve one import: emit diagnostic if unknown, mark as seen
resolveImport(known RBMap[Str][Bool])(modName Str) =
  stateBind (stateGet (emptyResolverState())) \s.
  if isSeen modName s
    statePure ()
  else
    match mapLookup strCmp modName known
      | None ->
        stateModify (addDiag (strConcat "E002 unknown module: " modName))
      | Some _ ->
        stateModify (markSeen modName)

-- Run resolver over a list of imports
resolveAll(known RBMap[Str][Bool])(imports List[Str]) =
  listFold
    (\acc imp. stateBind acc (\_ . resolveImport known imp))
    (statePure ())
    imports

-- Entry: returns accumulated diagnostics
checkImports(known RBMap[Str][Bool])(imports List[Str]) =
  s = stateExec (resolveAll known imports) (emptyResolverState())
  match s
    | MkResolverState _ diags -> diags
```

This sketch validates that `Std.State` + `Std.Map` + `Std.Maybe` compose correctly for
compiler-style pass work without fighting the language or requiring ad hoc helpers.
