# Architecture overview

The compiler is a straight pipeline: source text in, F# source out. Each
stage is a pure function (modulo the small `InferState` used in HM) and
returns `Result<_, LLError list>`.

## Pipeline

```
.lll source (string)
        │
        ▼
   Lexer.tokenize          Tok list with INDENT/DEDENT
        │
        ▼
   Parser.parseModule      LLModule : AST
        │
        ▼
   Elaborator.elaborate    TypeEnv + E001..E005 checks
        │
        ▼
   HMInfer.infer           TypedModule : typed AST + Env + dispatch map
        │
        ▼
   Codegen.emit            F# source string
        │
        ▼
  .fs file  →  dotnet fsi  (lllc run)
           →  dotnet build (lllc build)
```

The entry point `Compiler.compile : string -> Result<string, LLError list>`
chains the five stages together. Any stage can short-circuit with `Error`.

## Module-by-module

### `Token.fs` — token types

Enum-like discriminated union with cases for keywords, identifiers, literals,
operators, and the three synthetic layout tokens (`Indent`, `Dedent`,
`Newline`). A `Tok` carries source `Line` and `Col`.

### `Lexer.fs` — tokenizer

Character-by-character scan producing a `Tok list`. Responsible for:

- Keyword recognition via a `Map<string, Token>`.
- INDENT/DEDENT synthesis from leading whitespace changes.
- String literal escape handling.
- Distinguishing identifiers by case: `[A-Z]...` → `TypeId`, lowercase →
  `Ident`.

See [02-lexer](02-lexer.md).

### `AST.fs` — untyped surface AST

Discriminated unions for types (`TypeExpr`), literals (`Literal`), patterns
(`Pattern`), expressions (`Expr`), and declarations (`Decl`). An `LLModule`
is a record of `Path`, `Imports`, and `Decls`.

Type variables are `TyVar string`; type applications are `TyApp(outer, arg)`
built left-to-right; units are a separate `UnitExpr` tree nested inside
`TyTagged`.

### `Parser.fs` — recursive descent

Mutable `Ctx` cursor over a `Tok array`. Hand-written precedence climbing:
`parseAtom < parseTagged < parseApp < parseMul < parseAdd < parseCmp <
parsePipe`. Higher-level constructs (`parseExprInner`) handle `let`, `if`,
and lambda.

See [03-parser](03-parser.md).

### `Elaborator.fs` — declared-type checking pass

Three sub-passes:

1. `collectDecls` — walks decls, populates `TypeEnv : Map<string, TypeExpr>`
   with builtins, let-binding types (from literal inspection), function
   signatures, and sum-type constructors.
2. `checkDecls` — traverses function bodies calling `typeOf`, which walks
   expressions and compares declared vs actual types for each application.
   Emits E001/E004/E005 via `classifyMismatch`.
3. `exhaustivenessCheck` — for every `DFn` whose first param type is a
   named sum type, verifies that every `EMatch` branch list covers every
   constructor. Emits E003 for missing constructors.

Uses structural type equality with `TyVar` as a wildcard. Returns an
enriched `TypeEnv` that feeds into H-M.

See [04-elaborator](04-elaborator.md).

### `Types.fs` — type-scheme plumbing

Defines `TypeScheme`, `Subst` (flex-var-only), `Env`, `FreshState`. Exports
`applyType`, `applyEnv`, `compose`, `ftvType`, `generalize`, `instantiate`.
Rigid vars (no `$` prefix) are quantifiers; flex vars (`$N`) are
unification variables. The bridge `fromElaboratorEnv` converts
Elaborator's raw `TypeEnv` into H-M's `Env` by collecting rigid vars into
scheme quantifier lists.

### `TypedAST.fs` — typed AST

A parallel AST where every node has an `Id : ExprId`, a `Type : TypeExpr`,
and an `Expr : TypedExprKind`. Declarations carry a `TypeScheme` alongside
the body. `TypedModule` also tracks the final `Env` and a
`Dispatch : Map<ExprId, DispatchInfo>` (the trait dispatch table, populated
during inference).

### `HMInfer.fs` — Algorithm W

Walk the untyped `LLModule` producing a `TypedModule`. Key operations:

- `unify : TypeExpr -> TypeExpr -> Result<Subst, LLError>`
- `inferExpr : Env -> InferState -> Expr -> Subst * TypeExpr * TypedExpr`
- `inferDecl : Env -> InferState -> Decl -> bool -> (TypedDecl * bool) * Env`

Generalizes at every `let` and `fn`. Emits E001, E002, E004, E005, E006
(reserved), E008.

See [05-hm-inference](05-hm-inference.md).

### `Codegen.fs` — F# emitter

Walks the `TypedModule` producing a big string of F# source. Each
`TypedExprKind` case has a corresponding emit rule. Binary operators are
detected as `TEApp(TEApp(TEVar op, a), b)` and rendered infix. `fn main`
with no params becomes `[<EntryPoint>] let main (argv: string[]) = ... 0`.

See [06-codegen](06-codegen.md).

### `Compiler.fs` — pipeline glue

Twelve lines. Tokenize, parseModule, elaborate, infer, emit. Any failure
short-circuits with `Error`.

### `src/LLLangTool/Program.fs` — CLI

The `lllc` driver. Two commands:

- `build <file.lll>` — writes `<file>.fs` next to input.
- `run <file.lll>` — writes a temp `.fsx` (stripping `module` and
  `[<EntryPoint>]`, appending `main [||] |> exit`) and shells out to
  `dotnet fsi`.

## F# compile order

F# requires forward declarations through `<Compile Include="...">` order
in the `.fsproj`. The current order is significant:

```xml
<Compile Include="Token.fs" />
<Compile Include="Lexer.fs" />
<Compile Include="AST.fs" />
<Compile Include="Parser.fs" />
<Compile Include="Elaborator.fs" />
<Compile Include="Types.fs" />
<Compile Include="TypedAST.fs" />
<Compile Include="HMInfer.fs" />
<Compile Include="Codegen.fs" />
<Compile Include="Compiler.fs" />
```

Dependency chain:

- `Token.fs` → `Lexer.fs`
- `AST.fs` is independent; `Parser.fs` depends on `Token.fs` + `AST.fs`
- `Elaborator.fs` depends on `AST.fs` only
- `Types.fs` depends on `AST.fs` and (for `fromElaboratorEnv`) on
  `Elaborator.fs`
- `TypedAST.fs` depends on `AST.fs` + `Types.fs`
- `HMInfer.fs` depends on all of the above
- `Codegen.fs` depends on `AST.fs` + `Types.fs` + `TypedAST.fs`
- `Compiler.fs` glues everything together

If you add a new file, slot it in where its dependencies are satisfied.
Adding it at the end will usually work unless it is depended on by
`Codegen.fs` or `Compiler.fs`.

## No external parser / inference libraries

Everything is hand-written. The rationale:

- Future self-hosting (Phase 7) will translate this compiler into ll-lang
  itself. Avoiding external libraries keeps the translation mechanical.
- H-M is small (~200 lines) and the explicit form is easier to audit
  than an FParsec or FsLexYacc pipeline.
- Error messages are fully under our control — no generated parser spew.
