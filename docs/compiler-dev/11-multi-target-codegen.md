# Multi-Target Codegen

ll-lang supports three compilation targets: F# (default), TypeScript, and Python. This document covers the architecture of the multi-target system and how to add a new target.

## Architecture

All targets share the same pipeline up to H-M inference. Codegen is the only target-specific step:

```
Lexer → Parser → Elaborator → HMInfer → TypedAST
                                              ↓
                          ┌─────────────────────────────┐
                          │ Codegen    Codegen   Codegen  │
                          │ (F#)       (TS)      (Py)     │
                          └─────────────────────────────┘
```

The `Compiler.fs` dispatches to the right emitter:

```fsharp
type Target = FSharp | TypeScript | Python

let private compileSrc (emitter: TypedModule -> string) (src: string) =
    // ... lex → parse → elaborate → infer ...
    | Ok tm -> Ok (emitter tm)

let compile    = compileSrc Codegen.emit
let compileToTS = compileSrc CodegenTS.emit
let compileToPy = compileSrc CodegenPy.emit

let compileTarget target src =
    match target with
    | FSharp     -> compile src
    | TypeScript -> compileToTS src
    | Python     -> compileToPy src
```

**Critical:** do not add `open LLLang.CodegenTS` or `open LLLang.CodegenPy` to `Compiler.fs`. All three modules export `emit`; a broad open will shadow `Codegen.emit` and silently use the wrong backend. Use fully-qualified names instead.

## TypedAST IR

All emitters operate on `TypedModule` (from `TypedAST.fs`). Key types:

```fsharp
type TypedModule = {
    Path: ModulePath
    Decls: TypedDecl list
    Env: TypeEnv
}

type TypedDecl =
    | TDFn   of TypedFnSig * TypeVarId list * TypedExpr
    | TDLet  of Ident * TypeExpr * TypedExpr
    | TDLetPat of TypedPattern * TypedExpr
    | TDType of TypeIdent * TypeParam list * TypeBody
    | TDImpl of TypeIdent * TypeIdent * (TypedFnSig * TypeVarId list * TypedExpr) list
    | TDTag  of TypeIdent * TypeExpr * TypeIdent
    | TDTrait of ...
    | TDUnit of ...

type TypedExprKind =
    | TELit     of Lit
    | TEVar     of Ident
    | TECon     of TypeIdent
    | TEApp     of TypedExpr * TypedExpr
    | TELam     of Ident * TypedExpr
    | TELet     of Ident * TypeExpr * TypedExpr * TypedExpr option
    | TELetPat  of TypedPattern * TypedExpr * TypedExpr option
    | TEIf      of TypedExpr * TypedExpr * TypedExpr
    | TEMatch   of TypedExpr * (TypedPattern * TypedExpr) list
    | TEMatchOf of TypedExpr * (TypedPattern * TypedExpr) list
    | TEPipe    of TypedExpr * TypedExpr
    | TETuple   of TypedExpr list
    | TEList    of TypedExpr list
    | TECons    of TypedExpr * TypedExpr
    | TETagged  of TypedExpr * TypeIdent
```

`TypeBody` describes the shape of a type declaration:
- `TBSum` — algebraic sum type (list of `(ConstructorName, [TypeExpr])`)
- `TBRecord` — product type with named fields
- `TBWrapped` — single-field wrapper (e.g. for tag types)

## F# Codegen (`Codegen.fs`)

The F# backend is the reference implementation. It emits idiomatic F#:
- Sum types → discriminated unions
- Curried functions → `let f x y = ...`
- Pattern match → F# `match ... with`
- `[<EntryPoint>]` on `main`

## TypeScript Codegen (`CodegenTS.fs`)

Structure:
1. `emitType` — maps `TypeExpr` to TypeScript type strings
2. `emitLit`, `emitExprTS` — expression emitter
3. `emitSumTypeTS` — DU → discriminated union with `_tag` field
4. `emitFnTS` — curried arrow functions
5. `emitDecl` — per-declaration entry point
6. `tsPrelude` — stdlib bindings (appended after declarations)
7. `emitModule` / `emit` — top-level entry

### Type encoding

Sum types use tagged object unions. The discriminant field is `_tag` (backtick-quoted string literals for exact type inference):

```typescript
type Shape =
  { _tag: `Circle`; _0: number }
  | { _tag: `Rect`; _0: number; _1: number }
  | { _tag: `Empty` };
```

Zero-arg constructors emit as `const` objects; N-arg constructors as arrow functions.

### Stdlib map

`stdlibMap` in `CodegenTS.fs` maps ll-lang stdlib names to inline TypeScript expressions. When an expression is a known stdlib function application, the emitter inlines it instead of calling a Prelude function:

```fsharp
| "strLen"    -> "(s: string): number => s.length"
| "listMap"   -> "<A, B>(f: (x: A) => B) => (xs: A[]): B[] => xs.map(f)"
```

## Python Codegen (`CodegenPy.fs`)

Structure mirrors `CodegenTS.fs`:
1. `emitType` — maps to Python type strings
2. `safeIdent` — renames reserved words (`type` → `type_`, etc.)
3. `emitLit`, `emitExprPy` — expression emitter
4. `emitSumTypePy` — DU → `@dataclass` + `Union` alias
5. `emitFnPy` — curried nested `def`
6. `emitPattern` — pattern → Python destructure
7. `emitDecl` — per-declaration entry point
8. `pyPrelude` — `from __future__ import annotations` + stdlib (~60 lines)
9. `emitModule` / `emit` — top-level entry

### Curried functions

Python doesn't natively support curried functions in the same syntactic way. The backend uses nested defs:

```python
def add(a: int):
    def _f_b(b: int):
        return a + b
    return _f_b
```

The `buildCurried` helper recursively builds the nested `def` tree with increasing indentation.

### Pattern matching as expressions

Python's `match` statement is not an expression. The backend emits ternary if/else chains:

```python
(0 if c._tag == "Red" else (1 if c._tag == "Green" else 2))
```

### Reserved word escaping

Python has more reserved words than F# or TypeScript. `safeIdent` maps common conflicts:

| ll-lang | Python emitted |
|---------|---------------|
| `type`  | `type_`        |
| `class` | `class_`       |
| `from`  | `from_`        |
| `import`| `import_`      |
| `pass`  | `pass_`        |
| `None`  | `none_`        |

## Adding a New Target

1. **Create `CodegenX.fs`** in `src/LLLangCompiler/`. Export `let emit (tm: TypedModule) : string`.
2. **Add to `LLLangCompiler.fsproj`** before `Compiler.fs`.
3. **Extend `Target` DU** in `Compiler.fs` and add a case to `compileTarget`.
4. **Do NOT add a broad `open LLLang.CodegenX`** to `Compiler.fs` — use fully-qualified `CodegenX.emit`.
5. **Extend `parseTarget`** in `Program.fs` with the new flag alias.
6. **Write tests** in `tests/LLLangTests/CodegenXTests.fs`.

Minimum emitter structure:

```fsharp
module LLLang.CodegenX

open LLLang.TypedAST
open LLLang.Types

let private emitType (t: TypeExpr) : string = ...
let private emitLit (l: Lit) : string = ...
let rec private emitExpr (e: TypedExpr) : string = ...
let private emitDecl (d: TypedDecl) : string = ...

let private emitModule (tm: TypedModule) : string =
    let decls = tm.Decls |> List.map emitDecl |> String.concat "\n\n"
    prelude + decls

let emit (tm: TypedModule) : string = emitModule tm
```

## Test Coverage

Each backend has a dedicated test file:

| File | Backend | Tests |
|------|---------|-------|
| `CodegenTests.fs` | F# | ~40 |
| `CodegenTSTests.fs` | TypeScript | 20 |
| `CodegenPyTests.fs` | Python | 19 |

Tests verify:
- Type mapping correctness
- Sum type encoding (tag field, union alias)
- Function currying
- Let/const bindings
- If-then-else
- Pattern matching
- `compileTarget` dispatch
- Header comments
- Prelude/stdlib presence
