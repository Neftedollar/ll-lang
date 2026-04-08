# Parser

File: `src/LLLangCompiler/Parser.fs` (~690 lines).

A hand-written recursive-descent parser with a mutable cursor over a
`Tok array`. The `private Ctx` record holds the token array and a
mutable position index:

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
parseExprInner          -- let, if, \lambda, else delegate to parsePipe
    parsePipe           -- ->  (left-associative)
        parseCmp        -- < > <= >= == !=
            parseAdd    -- + -
                parseMul        -- * /
                    parseApp    -- juxtaposition (function application)
                        parseTagged   -- atom[Tag]
                            parseAtom -- literal, ident, paren, list
```

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
    | IntLit _ | FloatLit _ | StrLit _ | KwTrue | KwFalse
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

## The `parseFnSig` empty-paren quirk

`fn main()` — zero parameters expressed as empty parens — used to crash
the parser because the old code treated every `LParen` as the start of
a `parseParam`, which expected `(ident type)`. The fix (now in the
codebase):

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

Semantically `fn main()` is "a function named `main` with an empty
parameter list, inferred return type". The resulting `FnSig` has
`Params = []`. Downstream, Codegen special-cases this combination
(`sig_.Name = "main" && List.isEmpty sig_.Params`) into an
`[<EntryPoint>]` F# function.

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
`(Decl * bool)` (the boolean is `isExported`).

## Declaration dispatch

`parseDecl` branches on the leading keyword:

| Keyword    | AST node                                    |
|------------|---------------------------------------------|
| `fn`       | `DFn(FnSig, Expr)`                          |
| `let`      | `DLet(Ident, Expr)`                         |
| `type`     | `DType(TypeIdent, TypeParam list, TypeBody)` |
| `tag`      | `DTag(TypeIdent)`                           |
| `unit`     | `DUnit(TypeIdent)`                          |
| `trait`    | `DTrait(TypeIdent, Ident list, FnSig list)` |
| `impl`     | `DImpl(TypeIdent, TypeIdent, (FnSig * Expr) list)` |

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

`parsePattern` handles:

- `TypeId (args ...)` — constructor pattern. Subpatterns are atoms only
  (variables, literals, wildcards, or parenthesized patterns).
- `Ident` → `PVar`
- `Underscore` → `PWild`
- Literals (`IntLit`, `FloatLit`, `StrLit`, `KwTrue`, `KwFalse`) → `PLit`
- `(pat)` → parenthesized inner pattern

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
- Empty-paren `fn main()`.
- Pattern match branches with and without indentation.

Run:

```bash
dotnet test --filter ParserTests
```
