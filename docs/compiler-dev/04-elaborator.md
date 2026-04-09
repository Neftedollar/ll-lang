# Elaborator

File: `src/LLLangCompiler/Elaborator.fs` (~630 lines).

The elaborator is a pre-inference pass that does:

1. Module-level rewrites: `TyApp(T, TyVar t)` for any declared `tag t`
   is rewritten to `TyTagged(T, UName t)`.
2. Name resolution (module-level).
3. Declared-type checking (`E001`, `E002`, `E004`, `E005`).
4. Exhaustiveness of pattern matches (`E003`).

It runs after parsing and before H-M inference. Its signature is:

```fsharp
val elaborate : PosMap -> LLModule -> Result<LLModule * TypeEnv, LLError list>
```

On success it returns both the rewritten module and the enriched
`TypeEnv : Map<string, TypeExpr>`. The `TypeEnv` becomes the initial
environment for `HMInfer.fromElaboratorEnv`, and the `PosMap` is
threaded in so error emitters can look up real `line:col`.

## Core types

```fsharp
type TypeEnv = Map<string, TypeExpr>

type ErrorCode = E001 | E002 | E003 | E004 | E005 | E006 | E008

type LLError = {
    Code: ErrorCode
    Line: int
    Col: int
    Message: string  // compact format
}
```

`LLError` is shared with the H-M pass. `E007 PlatformMismatch` is
reserved in the design spec but has no runtime path.

## Three passes

### Pass 1: `collectDecls`

Walks every `Decl` and populates the `TypeEnv`. Starts with `builtinEnv`,
which covers all of the Phase 6 stdlib as well as the built-in operators:

```fsharp
let arithOps = [ "+"; "-"; "*"; "/" ]
let cmpOps   = [ "=="; "!="; "<"; ">"; "<="; ">=" ]
// Each arith op: a -> a -> a
// Each cmp op:   a -> a -> Bool
// printfn / print:      Str -> Unit
// math:                 abs, absf, sqrt, min, max
// list:                 listLen, listMap, listFilter, listFold,
//                       listHead, listTail, listReverse, listAppend,
//                       listConcat, listIsEmpty, listAt
// maybe:                maybeMap, maybeBind, maybeWithDefault
// result:               resultMap, resultBind, resultMapErr
// str / char:           strLen, strConcat, strTrim, strContains,
//                       strToInt, strChars, charToInt, intToChar,
//                       intToStr, strSlice, strIndexOf, strSplit,
//                       strFromChars, strReverse,
//                       charIsDigit, charIsAlpha, charIsSpace
// file IO:              readFile, writeFile, fileExists
// process:              exit
```

Arith and comparison ops are wildcard-polymorphic (`TyVar "a"`), which
lets the elaborator accept them without triggering E002 — they aren't
declared in source. The matching runtime bindings for the stdlib live
in `Codegen.fsharpPreludeCore` (plus the conditional `fsharpPreludeMaybe`
and `fsharpPreludeResult` blocks, emitted only when the user declares
`type Maybe` / `type Result`).

For each declaration:

- `DFn(sig, _body)` — add the curried function type to the env.
- `DLet(name, expr)` — inspect the literal-only initializer to infer
  a shallow type (int/float/str/char, bool via the KwTrue/KwFalse
  surface, or their tagged variants). Anything more complex becomes
  `TyVar "?"` (a wildcard) and is refined by HMInfer.
- `DLetPat(pat, _expr)` — bind every name introduced by the pattern as
  `TyVar "?"`; HMInfer refines to concrete types.
- `DType(name, params, TBSum ctors)` — add one entry per constructor,
  with a curried function type from the constructor args to the fully
  applied type (e.g. `type Maybe A = Some A | None` gives
  `Some : A -> Maybe[A]` and `None : Maybe[A]`).
- `DImpl(trait, type, fns)` — add each impl method under a mangled
  name `methodName_TypeName`.
- `DTag`, `DUnit`, `DTrait`, record types, and wrapped types do not
  add entries at this stage.

### Pass 2: `checkDecls` (via `typeOf`)

For each `DFn`, extend the env with the function's parameters (typed
from the signature, normalized via `normalizeFnTy` which converts
single-uppercase `TyName A` to `TyVar A`), then walk the body with
`typeOf`.

`typeOf : Expr -> TypeEnv -> TypeExpr * LLError list` walks the
expression and:

- Returns the literal type for each `ELit`.
- Looks up `EVar` / `ECon` and emits `E002 UnboundVar` on miss.
- At each `EApp(f, arg)`, extracts the parameter type from `f`'s
  `TyFn(paramType, returnType)` and compares it against `argType`.
- On mismatch, calls `classifyMismatch`.

The key equality check is `tyEqual`, which treats `TyVar _` as a
wildcard — so fully polymorphic arithmetic ops (`+`, `-`, etc.) accept
anything. This is intentional: the elaborator only catches mismatches
involving fully known types.

### `classifyMismatch` — triaging E001 / E004 / E005

Given a parameter type and an argument type, decide which error code
applies:

```fsharp
match asTagged paramType, asTagged argType with
| Some(pBase, pTag), Some(aBase, aTag) when tyEqual pBase aBase && pTag <> aTag ->
    // Same base, different unit/tag
    match pBase with
    | TyName "Float" | TyName "Int" -> e004 ...   // UnitMismatch
    | _                              -> e001 ...  // TypeMismatch (e.g. Str[UserId] vs Str[Email])
| Some(pBase, _), None when tyEqual argType pBase ->
    // Arg has the right base but no tag → E005
    e005 ...
| _ ->
    e001 ...
```

Priorities:

1. Numeric tag mismatch → `E004 UnitMismatch`.
2. Non-numeric tag mismatch → `E001 TypeMismatch`.
3. Untagged value where tagged expected → `E005 TagViolation`.
4. Everything else → `E001`.

### Pass 3: `exhaustivenessCheck`

Builds a map `typeToCtors : Map<string, string list>` from every
`DType(name, _, TBSum ctors)`. For each `DFn` whose first parameter is
a `TyName T` in that map, recursively collects every `EMatch` block in
the body via `collectMatches` and checks whether each block covers
every constructor of `T`.

```fsharp
for c in requiredCtors do
    if not (List.contains c coveredCtors) then
        yield e003 0 0 typeName c
```

`collectMatches` is a structural recursion that descends into `EApp`,
`EIf`, `ELet`, `EPipe`, `EList`, `ETuple`, `ETagged`, and other
`EMatch` bodies — but explicitly not into `ELam` (lambdas get their own
scope and the first-param heuristic doesn't apply).

### Known limitations

- The first-param heuristic only catches exhaustiveness when the sum
  type is the first parameter of the function. Multi-scrutinee matches
  are not handled.
- Wildcard `_` is not yet treated as a catch-all in exhaustiveness — if
  you write `| _ -> default`, the check still complains about missing
  constructors unless one of them is also named. This is a known bug
  tracked for fix.
- Line/col info for most elaborator errors is now threaded through the
  parser's `PosMap` side-table (look for `posOf pm node` in
  `typeOf`). A few synthesized error sites still fall back to `0:0`
  when the AST node has no recorded position.

## Bridge to H-M

The elaborator output is the input to `HMInfer.fromElaboratorEnv`,
which converts `TypeEnv` (raw `TypeExpr`) into H-M `Env`
(`Map<Ident, TypeScheme>`). Each type is inspected for rigid
(non-`$`-prefixed) `TyVar`s, which are collected into the scheme's
quantifier list. Flex vars are not expected in elaborator output.

```fsharp
let fromElaboratorEnv (e3: LLLang.Elaborator.TypeEnv) : Env =
    let collectRigid t = ...
    Map.map (fun _ ty ->
        let rigids = collectRigid ty |> Set.toList |> List.sort
        { Vars = rigids; Body = ty }
    ) e3
```

This is how `type Maybe A = Some A | None` ends up with
`Some : ∀A. A -> Maybe[A]` in the H-M environment.

## Tests

`tests/LLLangTests/ElaboratorTests.fs` covers:

- Each error code has a positive test that expects it from a specific
  invalid snippet.
- Valid programs from `spec/examples/valid/` are elaborated without
  errors.
- `classifyMismatch` triage: numeric vs non-numeric tag mismatches, and
  untagged-where-tagged cases.

Run:

```bash
dotnet test --filter ElaboratorTests
```
