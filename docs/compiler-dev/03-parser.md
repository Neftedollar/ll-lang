# Parser

File: `src/LLLangCompiler/Parser.fs` (~1230 lines).

A hand-written recursive-descent parser with a mutable cursor over a
`Tok array`. The `private Ctx` record holds the token array, a mutable
position index, and a `PosMap` side-table for attaching source
positions to AST nodes:

Note: this file documents the **legacy parser path**. The primary strict
front-end is `src/LLLangCompiler/FParsecParser.fs`; both paths currently
coexist for migration/parity checks.

```fsharp
type private Ctx = {
    Tokens: Tok array
    mutable Pos: int
}
```

All parser combinators return `Result<T, string>`. On error they leave
the cursor where it was and the module-level recovery simply advances
one token (`| Error _ -> advance c  // skip bad token`).

## Expression precedence

Climbing chain, from lowest to highest binding:

```
parseExprInner          -- let, if, match, \lambda, else delegate to parsePipe
    parsePipe           -- ->  (left-associative)
        parseCmp        -- < > <= >= == !=
            parseCons   -- ::  (right-associative)
                parseAdd        -- + -
                    parseMul    -- * /
                        parseApp        -- juxtaposition (function application)
                            parseTagged -- atom[Tag]
                                parseAtom -- literal, ident, paren, list
```

`parseBlockExpr` is a layout-aware wrapper used for bodies where an
`Indent`-terminated block of statements (`let ... let ... expr`) is
allowed. It delegates to `parseExprInner` for each statement.

Binary operators are desugared on the fly into nested `EApp` nodes:

```fsharp
result <- EApp(EApp(EVar opName, result), right)
```

The operator name is an `EVar` (`"+"`, `"<"`, etc.) — these are looked
up in `builtinEnv` during elaboration so they never raise E002.

## Juxtaposition = application

`parseApp` keeps consuming atom tokens as long as the next token can
start one:

```fsharp
while cont do
    match curTok c with
    | IntLit _ | FloatLit _ | StrLit _ | CharLit _ | KwTrue | KwFalse
    | Ident _ | TypeId _ | LParen | LBrack -> ... EApp(result, arg)
    | _ -> cont <- false
```

Left-associative by construction (`result` accumulates on the left).

## Tagged literals

`parseTagged` is a thin wrapper that, after reading an atom, checks for
`LBrack TypeId RBrack` and wraps the atom in `ETagged`. On failure it
backtracks:

```fsharp
let saved = c.Pos
advance c
match curTok c with
| TypeId name ->
    advance c
    match skip c RBrack with
    | Ok () -> Ok (ETagged(atom, name))
    | Error _ -> c.Pos <- saved; Ok atom
```

## Pattern matching branches

`parseMatchBranches` consumes a sequence of `| pat -> expr` lines:

```fsharp
while cont && curTok c = Bar do
    advance c
    match parsePattern c with
    | Ok pat ->
        match skip c Arrow with
        | Ok () ->
            match parseExprInner c with
            | Ok expr -> branches.Add((pat, expr)); skipNewlines c
```

It's called from `parseFnBody` whenever the body starts with `Bar` (or
with `Indent` followed by `Bar`).

## Type expressions

`parseTypeExpr` has its own precedence chain:

```
parseTypeExprTop        -- type arrow A -> B (right-associative)
    parseApp            -- Maybe[Int] — repeated [type]
        parseBase       -- TypeId, Ident (type var), (parenthesized)
```

`LBrack` after a base type starts a parametric application. The
ambiguity with the start of a tagged expression in `parseAtom` is
avoided by context: `parseTypeExpr` is only called inside type positions.

## The zero-param function / empty-paren quirk (historical)

In earlier ll-lang the `fn main()` form — zero parameters expressed as
empty parens — used to crash the parser because the old code treated
every `LParen` as the start of a `parseParam`, which expected
`(ident type)`. The fix (still in the stage0 codebase):

```fsharp
while paramCont && curTok c = LParen do
    let saved = c.Pos
    advance c
    if curTok c = RParen then
        advance c
        // `()` group contributes zero params; continue loop to allow mixing.
    else
        c.Pos <- saved
        match parseParam c with
        | Ok p -> parms.Add(p)
        | Error _ -> c.Pos <- saved; paramCont <- false
```

In **v2**, `fn` was removed. A zero-parameter entry point is written as
a bare value binding: `main = ...`. The resulting `FnSig` still has
`Params = []`. Codegen special-cases `sig_.Name = "main" && List.isEmpty sig_.Params`
into `[<EntryPoint>] let main (argv: string[]) = ...`.

## Module parsing

`parseModule` follows a fixed shape:

1. Expect `module` keyword.
2. Read dotted path (`TypeId (Dot TypeId)*`).
3. Loop over `import` declarations.
4. Loop over remaining tokens: optional `export` prefix, then `parseDecl`.
5. On decl error, advance one token and keep trying (best-effort
   recovery so that cascading errors all surface at once).
6. Terminate on `Eof`.

Output: `LLModule { Path; Imports; Decls }` where `Decls` is a list of
`(Decl * bool)` (the boolean is `isExported`). Two public entry points
exist: `parseModule` (returns just the module) and
`parseModuleWithPos` (returns the module plus the populated `PosMap`
that the elaborator and HMInfer read when emitting errors).

## Declaration dispatch

`parseDecl` dispatches on the leading token. In the stage0 bootstrap the
AST still uses `DFn`/`DType` nodes internally; in v2 surface syntax the
`fn` and `type` keywords are gone and functions/types are recognized by
leading `IDENT (params…) =` and `TYPEID =` patterns:

| Surface (v2) / Stage0 trigger   | AST node                                         |
|----------------------------------|--------------------------------------------------|
| `IDENT ( param )+ = body`        | `DFn(FnSig, Expr)` — function declaration        |
| `IDENT = expr` (no params)       | `DLet(Ident, Expr)` — value binding / entry point |
| `TYPEID type-params? = type-body`| `DType(TypeIdent, TypeParam list, TypeBody)`     |
| `tag TYPEID`                     | `DTag(TypeIdent)`                                |
| `unit TYPEID`                    | `DUnit(TypeIdent)`                               |
| `trait TYPEID …`                 | `DTrait(TypeIdent, Ident list, FnSig list)`      |
| `impl TYPEID TYPEID …`           | `DImpl(TypeIdent, TypeIdent, (FnSig * Expr) list)` |

`DLet` is parsed pattern-first: a bare `PVar` falls back to `DLet` so
all existing code paths keep working, while non-trivial patterns
(tuples, cons, constructors) produce `DLetPat`.

## Type body parsing

`parseTypeBody` disambiguates between sum, record, and wrapped types:

- Leading `TypeId` → sum (`Ctor args | Ctor args | ...`).
- Leading `Ident` followed by a type → record (`name Type, name Type`).
- Anything else → wrapped (`type Foo = Int`), treated as a single-arg
  newtype.

The "record vs wrapped" decision is tentative — after reading
`fieldName typeExpr` it checks for a comma; if missing and the first
field was a bare wrapped type, it falls back to `TBWrapped`.

## Pattern parsing

`parsePattern` is split into two levels:

- `parsePatCons` — top-level, handles right-associative `::`
  (`h :: t`) producing `PCons(h, t)`.
- `parsePatAtom` — atoms:
  - `TypeId (args ...)` — constructor pattern `PCon(name, args)`.
  - `Ident` → `PVar`
  - `Underscore` → `PWild`
  - Literals (`IntLit`, `FloatLit`, `StrLit`, `CharLit`, `KwTrue`,
    `KwFalse`) → `PLit`
  - `(pat)` → parenthesized inner pattern; `(p1, p2, ..., pN)` becomes
    `PTuple`.
  - `[]` → empty-list pattern, emitted as `PCon("[]", [])` so downstream
    pattern emission can special-case it back to F#'s `[]`.

Constructor subpatterns do not themselves accept constructor args without
parens — to nest constructors you must write `Some (Cons x xs)`.

## Error recovery

The parser is opportunistic: on a bad decl it calls `advance c` and
resumes at the next token. This means a single syntax error rarely
derails the whole file, which is important for LLM-driven fix loops —
the compiler can surface multiple errors per pass.

## Tests

`tests/LLLangTests/ParserTests.fs` covers:

- Every decl form.
- Expression precedence (ordering of `+`, `*`, `->`, etc.).
- Record vs sum vs wrapped type bodies.
- Zero-param entry point (`main = ...` as a bare value binding).
- Pattern match branches with and without indentation.

Run:

```bash
dotnet test --filter ParserTests
```
