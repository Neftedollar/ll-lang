# Code generation

File: `src/LLLangCompiler/Codegen.fs` (~210 lines).

Walks a `TypedModule` and produces a single F# source string. No
intermediate representation — direct AST-to-string emission.

## Entry point

```fsharp
let emit (tm: TypedModule) : string = emitModule tm
```

`emitModule` produces:

```
module <dotted.path>

<decl1>

<decl2>
...
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
- Type variables `TyVar v` become `'v` (F# syntax for generic type
  parameters).
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
```

The float handling appends `.0` to integer-valued floats so F# doesn't
treat them as `int`. Strings are quoted and escape sequences are
re-applied.

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

| Node                    | Output                                              |
|-------------------------|-----------------------------------------------------|
| `TELit l`               | `emitLit l`                                         |
| `TEVar x`               | `safeIdent x`                                       |
| `TECon c`               | `safeIdent c`                                       |
| `TEApp(f, a)` (binop)   | `(a op b)`                                          |
| `TEApp(f, a)`           | `(f a)`                                             |
| `TELam(ps, body)`       | `(fun p1 p2 -> body)`                               |
| `TELet(x, _, e, Some b)`| `(let x = e in\n  b)`                               |
| `TELet(x, _, e, None)`  | `(let x = e)`                                       |
| `TEIf(c, t, e)`         | `(if c then t else e)`                              |
| `TETagged(e, _)`        | `emitExpr e` (tag dropped)                          |
| `TEList es`             | `[e1; e2; e3]`                                      |
| `TETuple es`            | `(e1, e2, e3)`                                      |
| `TEPipe(a, b)`          | `(b a)` — pipe becomes forward application         |
| `TEMatch(scrut, brs)`   | `(match scrut with\| p -> body\| ...)`              |

All emitted expressions are wrapped in parens to sidestep precedence
surprises in the target F#.

## Pattern emission

```fsharp
let rec private emitPattern (p: Pattern) : string =
    match p with
    | PVar x   -> safeIdent x
    | PWild    -> "_"
    | PLit l   -> emitLit l
    | PCon(c, [])  -> safeIdent c
    | PCon(c, [p]) -> safeIdent c + " " + emitPattern p
    | PCon(c, ps)  -> safeIdent c + "(" + (ps |> List.map emitPattern |> String.concat ", ") + ")"
```

Single-arg constructors go space-separated (`Some x`); multi-arg use
parenthesized tuple form (`Rect(w, h)`), which matches F# DU pattern
syntax for multi-field cases.

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
    let isMain = sig_.Name = "main" && List.isEmpty sig_.Params
    let isRec = containsVar sig_.Name body
    let recKw = if isRec then "rec " else ""
    ...
    if isMain then
        "[<EntryPoint>]\nlet main (argv: string[]) =\n    " + bodyStr + "\n    0"
    else
        "let " + recKw + safeIdent sig_.Name + paramPart + " =\n    " + bodyStr
```

Three decisions:

1. `fn main()` (zero params) becomes F#'s `[<EntryPoint>] let main
   (argv: string[]) = ...` — the program's entry point.
2. `containsVar` scans the body for a reference to the function's own
   name. If found, emit `let rec`.
3. Otherwise: normal `let` with space-joined parameter names.

`containsVar` is a simple structural recursion over `TypedExpr` that
looks for `TEVar name` matching the function's own name.

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

`safeIdent` wraps reserved F# keywords in double backticks to prevent
collisions:

```fsharp
let private fsKeywords = Set.ofList [ "abstract"; "and"; "as"; ... ]

let private safeIdent (s: string) =
    if Set.contains s fsKeywords then "``" + s + "``" else s
```

So a ll-lang function called `function` would emit as `` ``function`` ``
in the output — the F# compiler accepts it.

## `llc run` and `dotnet fsi` quirks

F# interactive (`dotnet fsi`) does not honor `[<EntryPoint>]` or
module headers in script mode. `llc run` works around this by
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
debugging why `llc run` produces different behavior than `llc build`
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
