# Code generation

File: `src/LLLangCompiler/Codegen.fs` (~490 lines).

Walks a `TypedModule` and produces a single F# source string. No
intermediate representation — direct AST-to-string emission.

## Entry point

```fsharp
let emit (tm: TypedModule) : string = emitModule tm
```

`emitModule` splits declarations into two buckets so the auto-generated
F# prelude can reference user-declared `Maybe` / `Result` types when
present:

```
module <dotted.path>

<type decls>           -- emitted first so the prelude can see them

<prelude block>        -- core stdlib bindings (+ Maybe/Result when used)

<non-type decls>       -- fns, lets, impls (grouped for mutual recursion)
```

Declarations are joined with double newlines. Empty strings (returned
for `TDTag`, `TDUnit`, `TDTrait` which emit nothing) are filtered out.

## Type emission — `emitType`

```fsharp
let rec private emitType (t: TypeExpr) : string =
    match t with
    | TyName "Int"   -> "int64"
    | TyName "Float" -> "float"
    | TyName "Str"   -> "string"
    | TyName "Bool"  -> "bool"
    | TyName "Unit"  -> "unit"
    | TyName "Char"  -> "char"
    | TyName x when isTypeParamName x -> "'" + x  // single-uppercase A/B/C
    | TyName x       -> x
    | TyVar v        -> "'" + v
    | TyApp(TyName "List", a) -> emitType a + " list"
    | TyApp(f, a)    -> emitType a + " " + emitType f
    | TyFn(a, b)     -> emitType a + " -> " + emitType b
    | TyTagged(t, _) -> emitType t       // tags erase
```

Key points:

- ll-lang `Int` becomes F# `int64`. Integer literals are suffixed with
  `L`. A deliberate choice — ll-lang integers are 64-bit everywhere.
- ll-lang `Char` becomes F# `char`.
- Type variables `TyVar v` become `'v`. A bare single-uppercase
  `TyName A` (treated as a type parameter by the parser / normalizer)
  also emits as `'A` via `isTypeParamName`.
- `TyApp(TyName "List", a)` is special-cased to `a list`. Other type
  applications use F# postfix syntax `arg Outer`.
- `TyTagged` strips the tag entirely — units and newtype labels are
  compile-time only.

## Literal emission — `emitLit`

```fsharp
let private emitLit (l: Literal) : string =
    match l with
    | LInt n   -> string n + "L"
    | LFloat f ->
        let s = sprintf "%g" f
        if s.Contains('.') || s.Contains('e') || s.Contains('E') then s else s + ".0"
    | LStr s   -> // escape \\, ", \n, \r, \t then quote
    | LBool b  -> if b then "true" else "false"
    | LChar c  -> // escape \\, ', \n, \r, \t, \0 then quote
```

The float handling appends `.0` to integer-valued floats so F# doesn't
treat them as `int`. Strings and chars are quoted and escape sequences
are re-applied. `LChar` uses the F# single-quoted form (`'a'`,
`'\n'`).

## Binary operator mapping

Binary operator calls come through inference as
`TEApp(TEApp(TEVar op, a), b)`. Codegen detects this shape in the
`TEApp` case and renders infix:

```fsharp
| TEApp(outer, b) when (match outer.Expr with
                        | TEApp(inner, _) ->
                            (match inner.Expr with
                             | TEVar op -> binaryOp op <> None
                             | _ -> false)
                        | _ -> false) ->
    ... "(" + a + " " + fop + " " + b + ")"
```

The mapping table:

| ll-lang | F#  |
|---------|-----|
| `+`     | `+` |
| `-`     | `-` |
| `*`     | `*` |
| `/`     | `/` |
| `==`    | `=` |
| `!=`    | `<>`|
| `<`, `>`, `<=`, `>=` | identical |

Any `EVar op` with no entry in `binaryOp` is emitted as a normal
function call.

## Expression emission — `emitExpr`

Each `TypedExprKind` maps to a textual form:

| Node                       | Output                                              |
|----------------------------|-----------------------------------------------------|
| `TELit l`                  | `emitLit l`                                         |
| `TEVar x`                  | `safeIdent x`                                       |
| `TECon c`                  | `safeIdent c`                                       |
| `TEApp(f, a)` (binop)      | `(a op b)`                                          |
| `TEApp(f, a)` (multi-arg ctor) | `(C (a1, a2, ..., aN))`                         |
| `TEApp(f, a)` (plain)      | `(f a)`                                             |
| `TELam(ps, body)`          | `(fun p1 p2 -> body)`                               |
| `TELet(x, _, e, Some b)`   | `(let x = e in b)` (single-line form)               |
| `TELet(x, _, e, None)`     | `(let x = e)`                                       |
| `TELetPat(tp, e, Some b)`  | `(let <pat> = e in b)` (single-line)                |
| `TELetPat(tp, e, None)`    | `(let <pat> = e)`                                   |
| `TEIf(c, t, e)`            | `(if c then t else e)`                              |
| `TETagged(e, _)`           | `emitExpr e` (tag dropped)                          |
| `TEList es`                | `[e1; e2; e3]`                                      |
| `TETuple es`               | `(e1, e2, e3)`                                      |
| `TECons(h, t)`             | `(h :: t)`                                          |
| `TEPipe(a, b)`             | `(b a)` — pipe becomes forward application          |
| `TEMatch(scrut, brs)`      | multi-line `(match scrut with\n  \| p -> body\n  \| ...)` |
| `TEMatchOf(scrut, brs)`    | single-line `(match scrut with \| p -> body \| ...)` |

All emitted expressions are wrapped in parens to sidestep precedence
surprises in the target F#. The single-line `let` / `let-pat` / match
forms sidestep F# offside-rule issues when the construct is nested inside
another expression at an arbitrary indentation column.

Multi-arg ADT constructors are a special case of `TEApp`: ll-lang
treats them as curried (`MkPair x y`) but F# requires a tuple argument
(`MkPair(x, y)`), so codegen walks through nested `TEApp`s to gather
arguments and emits a single tuple call when the head is a `TECon`.

## Pattern emission

```fsharp
let rec private emitPattern (p: Pattern) : string =
    match p with
    | PVar x   -> safeIdent x
    | PWild    -> "_"
    | PLit l   -> emitLit l
    | PCon("[]", []) -> "[]"  // parser's empty-list sentinel
    | PCon(c, [])  -> safeIdent c
    | PCon(c, [p]) -> safeIdent c + " " + emitPattern p
    | PCon(c, ps)  -> safeIdent c + "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PTuple ps    -> "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
    | PCons(h, t)  -> "(" + emitPattern h + " :: " + emitPattern t + ")"
```

Single-arg constructors go space-separated (`Some x`); multi-arg use
parenthesized tuple form (`Rect(w, h)`), which matches F# DU pattern
syntax for multi-field cases. `PCon("[]", [])` is a sentinel emitted by
the parser for empty-list patterns and must render as F#'s `[]`
literal, not as an ordinary ctor reference.

## Declaration emission — `emitDecl`

### Sum and record types

```fsharp
| TDType(name, ps, body) ->
    let params' = emitTypeParams ps
    let header = "type " + name + params' + " ="
    match body with
    | TBSum branches ->
        // | Circle of float
        // | Rect of float * float
        // | Empty
    | TBRecord fields ->
        // type Point = { x: float; y: float }
    | TBWrapped t ->
        // type Name = | Name of t   (newtype-style single-case DU)
```

Type parameters: bare `A` in ll-lang becomes `<'A>` in F#. Phantom
params (`[state]`) are dropped — they have no F# equivalent and exist
only for the elaborator/inference to distinguish types.

### Function declarations

```fsharp
| TDFn(sig_, _, body) ->
    if isMainFn sig_ then
        "[<EntryPoint>]\nlet main (argv: string[]) =\n    " + bodyStr + "\n    0"
    else
        let isRec = containsVar sig_.Name body
        let recKw = if isRec then "rec " else ""
        "let " + recKw + emitFnClause sig_ body
```

Three decisions:

1. `fn main()` (zero params) becomes F#'s `[<EntryPoint>] let main
   (argv: string[]) = ...` — the program's entry point.
2. `containsVar` scans the body for a reference to the function's own
   name. If found, emit `let rec`.
3. Otherwise: normal `let` with space-joined parameter names.

`containsVar` is a structural recursion over `TypedExpr` that looks
for `TEVar name` matching a given name.

### Mutual recursion grouping

Runs of consecutive non-`main` top-level function decls are passed
through `groupDecls`, which partitions them into mutually-recursive
groups. A group of two or more functions is emitted as a single F#
`let rec <first> ... and <rest>` block **iff** at least one function
in the run references another function from the same run. Runs where
no cross-reference exists are split back into singletons so existing
output stays as plain `let` definitions (no unnecessary `rec`).

This is necessary because HMInfer's top-level pass already infers
mutually-recursive sibling fns together — without the codegen
grouping, the emitted F# would fail to resolve forward references.

### Top-level lets

```fsharp
| TDLet(x, _, e)        -> "let " + safeIdent x + " = " + emitExpr 0 e
| TDLetPat(tp, e)       -> "let " + emitPattern tp.Pat + " = " + emitExpr 0 e
```

### Tag and trait decls

```fsharp
| TDTag _  -> ""
| TDUnit _ -> ""
| TDTrait _ -> ""
```

Empty strings. Filtered out when joining decl output. Tags and traits
are purely compile-time constructs.

### Impl decls

```fsharp
| TDImpl(_, typeName, methods) ->
    methods |> List.map (fun (sig_, _, body) ->
        ...
        "let " + recKw + safeIdent typeName + "_" + safeIdent sig_.Name + paramPart + " = ..."
    ) |> String.concat "\n\n"
```

Each impl method becomes a top-level `let` binding named
`TypeName_methodName` (e.g. `Maybe_map`). This is the mangling that
parallels the one in `HMInfer.fs` for environment lookups.

## F# keyword safety

`safeIdent` rewrites ll-lang identifiers that collide with F# keywords
OR with F#'s "reserved for future use" word list (FS0046 —
`params`, `object`, `functor`, ...) to a prefixed safe form:

```fsharp
let private fsKeywords = Set.ofList [ "abstract"; "and"; "as"; ...;
                                      "params"; "object"; "functor"; ... ]

let private safeIdent (s: string) =
    if Set.contains s fsKeywords then "__ll_" + s else s
```

So a ll-lang parameter called `params` emits as `__ll_params` in the
output. The `__ll_` prefix is used instead of backtick-quoting because
backticks do **not** suppress the FS0046 "reserved for future use"
warning on words like `object` and `params`.

## F# prelude block

`assemblePrelude` prepends an auto-generated F# prelude to every
emitted module. It has three parts:

- `fsharpPreludeCore` — always emitted. Contains the runtime
  definitions for every stdlib function exposed via `builtinEnv`
  that has no dependency on user-declared types (math, list-without-
  Maybe, str, char, file IO, process, print).
- `fsharpPreludeMaybe` — emitted only when the user module declares
  `type Maybe`. Defines `listHead`, `listTail`, `maybeMap`,
  `maybeBind`, `maybeWithDefault`, `strToInt`, `listAt` — all of
  which return `Maybe`-shaped values and therefore need the user's
  `Some`/`None` constructors in scope.
- `fsharpPreludeResult` — emitted only when the user module declares
  `type Result`. Defines `resultMap`, `resultBind`, `resultMapErr`.

The prelude is emitted AFTER the user's type declarations so that
`Maybe`/`Result`-dependent helpers resolve to the user's own types
rather than F#'s built-in `Option` / `Result`. The core prelude is
also exposed as `Codegen.preludeBlock` for tests.

## `lllc run` and `dotnet fsi` quirks

F# interactive (`dotnet fsi`) does not honor `[<EntryPoint>]` or
module headers in script mode. `lllc run` works around this by
post-processing the emitted source:

```fsharp
let stripped =
    fs.Split('\n')
    |> Array.filter (fun l ->
        let t = l.TrimStart()
        not (t.StartsWith("module ")) && not (t.StartsWith("[<EntryPoint>]")))
    |> String.concat "\n"
let withInvoke = stripped + "\nmain [||] |> exit\n"
```

It drops the `module` line and the `[<EntryPoint>]` attribute, then
appends an explicit `main [||] |> exit` call so the main function
actually runs. This is invisible to users but important if you're
debugging why `lllc run` produces different behavior than `lllc build`
followed by `dotnet fsc`.

## Known gaps

- **No closure conversion.** Lambdas emit directly as F# lambdas, which
  is fine on .NET but won't translate to all future backends.
- **No tail-call optimization hint.** F# does its own TCO; we don't
  insert `[<TailCall>]` attributes even when it would help.
- **Pipe codegen is `(b a)`, not `(a |> b)`.** Equivalent at runtime
  but less idiomatic. Switching to `|>` would require care with
  curried multi-arg functions.
- **Match scrutinee from `EMatch` at expression position** emits as a
  generated lambda `fun $scrut -> match $scrut with ...`. Works but
  readable F# would use a direct `match` expression.

## Tests

`tests/LLLangTests/CodegenTests.fs` uses the helper:

```fsharp
let private codegenSrc (src: string) : string =
    match LLLang.Compiler.compile src with
    | Ok fs -> fs
    | Error es -> failwith $"codegen failed: {es}"
```

Tests assert containment of specific strings (`Assert.Contains`)
rather than byte-equality, so small formatting changes don't break
them.

Run:

```bash
dotnet test --filter CodegenTests
```
