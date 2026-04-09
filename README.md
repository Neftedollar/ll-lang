# ll-lang

[![Build & Test](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml/badge.svg)](https://github.com/Neftedollar/ll-lang/actions/workflows/build.yml)

> **A statically-typed functional language designed for LLM code generation.** Token-efficient syntax, compiled = works, and errors formatted for LLMs to read directly.

```
module Hello

fn main() = printfn "Hello, ll-lang!"
```

```
$ lllc run hello.lll
Hello, ll-lang!
```

Jump to [Problem](#problem), [Solution](#solution), [Syntax](#syntax), [Getting Started](#getting-started).

## Status

Working end-to-end compiler with a **518-test** suite, written in F# / .NET 10. All 10 compiler phases green: lexer → parser → elaborator → Hindley-Milner inference → F# codegen → `lllc` CLI → stdlib (~50 builtins) → module system → MCP server → TypeScript + Python + Java codegen.

**Bootstrap: COMPLETE (Phase 7.10).** `compiler₁.fs == compiler₂.fs` — ll-lang compiles itself.

**Bootstrap progress (Phase 7 — ll-lang hosting itself):**

| Artifact | Shape | Status | Source |
|---|---|---|---|
| Lexer | multi-char idents, keywords, ops | ✅ | [`09-lexer-real.lll`](spec/examples/valid/09-lexer-real.lll) |
| Arithmetic parser | `+ - * /`, precedence, parens | ✅ | [`11-parser-real.lll`](spec/examples/valid/11-parser-real.lll) |
| Type-decl parser | sum types, type params | ✅ | [`12-typeparser-real.lll`](spec/examples/valid/12-typeparser-real.lll) |
| Fn-decl parser | curried params, return types | ✅ | [`13-fnparser-real.lll`](spec/examples/valid/13-fnparser-real.lll) |
| Expression parser | let / if / match / lambda / app | ✅ | [`14-exprparser-real.lll`](spec/examples/valid/14-exprparser-real.lll) |
| **Full module parser** | **all of the above, in one program** | ✅ | [`15-moduleparser-real.lll`](spec/examples/valid/15-moduleparser-real.lll) |
| Elaborator — name res + exhaustiveness | collect + check passes, E002 unbound-var, E003 non-exhaustive match | ✅ | [`16-elaborator-real.lll`](spec/examples/valid/16-elaborator-real.lll) |
| **Parser + Elaborator pipeline** | **lex → parse → elaborate in one ll-lang program** | ✅ | [`17-pipeline-real.lll`](spec/examples/valid/17-pipeline-real.lll) |
| HM inference (unify + inferExpr) | `TypeExpr` AST + `Subst` + `unify` (with **occurs check**) + `Env` of **type schemes** + `freshVar` + `inferExpr` over literal / var / EAdd / EApp / ELam / ELet (with **let-gen**) / EIf / **EMatch**; **Result-threaded errors** (E001 / E002 / E008) | ✅ | [`18-hminfer-real.lll`](spec/examples/valid/18-hminfer-real.lll) |
| **Codegen (TExpr → F#, full codegen 7.8a-e)** | `TExpr` / `TDecl` / `Pat` / `Ctor` / `TypeArg` / `Module` AST + `showTExpr` / `showDecl` / `showModule` emitting F# source for TEInt / TEStr / TEVar / TEAdd / TEApp / TELet + TELam + TEIf + TEMatch / TDFn / TDType sum decls + `module <path>` header + F# stdlib prelude block + **mutually-recursive `let rec ... and ...` grouping + `[<EntryPoint>]` on zero-param `main`** | ✅ | [`19-codegen-real.lll`](spec/examples/valid/19-codegen-real.lll) |
| **4-stage bootstrap compiler** | **parser + elaborator + minimal HM type checker + F# codegen in one program** (lex → parse → elab → HM → emit F# source; on a clean module the pipeline now produces a compilable F# file with `module` header + stdlib prelude + sum-type decls + `let rec ... and ...` group + `[<EntryPoint>]` main wrapper) | ✅ | [`20-bootstrap-compiler.lll`](spec/examples/valid/20-bootstrap-compiler.lll) |
| **Reads source from file** | **`readFile` call in the driver replaces the hardcoded source string — bootstrap compiler is now a real file-reading tool** | ✅ | [`20a-bootstrap-input.lll`](spec/examples/valid/20a-bootstrap-input.lll) |

The module parser (979 lines of ll-lang) consumes `module M \n import ... \n tag ... \n type ... \n let ... \n fn ... = ...` and pretty-prints a `List[Decl]` AST — **real proof that ll-lang can express its own front-end**. The elaborator slice (512 lines) walks a hardcoded `List[Decl]` AST with a two-pass `collectDecls` → `checkDecls` pipeline plus an exhaustiveness pass, and emits `E002 UnboundVar <name>` for every free variable and `E003 NonExhaustiveMatch <type> missing <ctor>` for every clause-sugar match that doesn't cover its sum type — mirroring the F# host elaborator's name-resolution + constructor-coverage semantics. The pipeline slice (~1500 lines) stitches the two halves into one program: a source string goes in, `tokenize` + `parseModule` + `elaborate` runs back-to-back, and the resulting error list is printed. **Showcase milestone** — first time two compiler layers authored in ll-lang share a single AST and run back-to-back on a real source string.

**Phase 7 complete** — bootstrap fixpoint achieved (2026-04-09). **Phase 8 complete** — module system with `ll.toml`, multi-file builds, `lllc new`. **Phase 9 complete** — MCP server (`lllc mcp`) with 8 tools for LLM clients. **Phase 10 complete** — TypeScript, Python, and Java codegen (`lllc build --target ts|py|java`).

| Phase | Description | Status |
|---|---|---|
| 1 | Spec (grammar + corpus) | ✅ |
| 2 | Lexer + Parser | ✅ |
| 3 | Elaborator (exhaustiveness, tag/unit checks) | ✅ |
| 4 | Hindley-Milner + TypedAST + trait dispatch | ✅ |
| 5 | F# codegen + `lllc` CLI | ✅ |
| 6 | Stdlib (~50 builtins) | ✅ |
| **7** | **Bootstrap fixpoint** — ll-lang compiles itself (`compiler₁.fs == compiler₂.fs`) | ✅ |
| **8** | **Module system** — `ll.toml`, multi-file builds, `lllc new`, topo-sort, E020/E024 | ✅ |
| **9** | **MCP server** — `lllc mcp` stdio server with 8 tools for Claude Code / Cursor / Zed | ✅ |
| **10** | **Multi-platform codegen** — `lllc build --target ts\|py\|java`; TypeScript DU + Python @dataclass + Java sealed interfaces | ✅ |
| **7.1 – 7.5** | **ll-lang front-end in ll-lang** (lexer + 4 parser slices + full module parser, 979 lines) | ✅ |
| **7.6a + 7.6b** | **Elaborator slices A + B in ll-lang** (name resolution + E002 unbound-var; constructor-coverage exhaustiveness + E003 non-exhaustive match, 512 lines) | ✅ |
| **7.6 integration** | **Parser + elaborator pipeline in one ll-lang program** (`17-pipeline-real.lll`, ~1500 lines) | ✅ |
| **7.7 (a → d)** | **HM inference rewritten in ll-lang** — `TypeExpr`, `Subst`, `unify` (with occurs check), `Env` of type schemes, `freshVar`, `composeSubst`, `Expr` (EInt / EStr / EBool / EVar / EAdd / EApp / ELam / ELet / EIf / EMatch), `Pat` (PInt / PVar / PWild), `Outcome`-threaded errors (E001 / E002 / E008), let-generalization (`generalize` / `instantiate` / `ftvType` / `ftvEnv`). `18-hminfer-real.lll`, ~1010 lines | ✅ |
| **7.8 (a → e)** | **Full codegen rewritten in ll-lang** — `TExpr` / `TDecl` / `Pat` / `Ctor` / `TypeArg` / `Module` AST + `showTExpr` / `showDecl` / `showModule` emitting F# source for TEInt / TEStr / TEVar / TEAdd / TEApp / TELet / TELam + TEIf + TEMatch (single-line match form) / TDFn / TDType sum-type decls (multi-line, parametric headers) + `module <path>` header + F# stdlib prelude block (5-binding subset of `fsharpPreludeCore`) + **mutually-recursive `let rec ... and ...` grouping for 2+ consecutive non-main TDFns + `[<EntryPoint>]` emission on zero-param `main`**; `19-codegen-real.lll`, 926 lines. Output is now a complete, compilable F# source file | ✅ |
| **7.9a** | **First 3-stage bootstrap compiler** — parser + elaborator + minimal HM type checker in one ll-lang file (`20-bootstrap-compiler.lll`, 1598 lines). Adds `TypeExpr` / `typeEq` / `inferExprType` / `typeCheck` on top of `17-pipeline-real.lll` and emits `E001 TypeMismatch` for arithmetic / if-branch mismatches alongside the existing `E002` / `E003` output. First time three compiler stages run back-to-back on a shared AST inside a single ll-lang program. Deliberately narrow — no `unify` / `Subst` / fresh vars (punted to 7.9b), no `ELam` / `ELetIn` / `EMatch` type checking | ✅ |
| **7.9b** | **4-stage bootstrap compiler** — adds an `emit*` codegen pass to `20-bootstrap-compiler.lll` (1598 → 2100 lines). Walks the parser's `Decl` / `Expr` / `Pat` / `TypeArg` AST and produces a complete F# source file (`module` header + auto-generated stdlib prelude block + sum-type decls + `let rec ... and ...` non-main fn group + `[<EntryPoint>]`-wrapped `main` fn). The hardcoded driver source is now intentionally clean (no errors), so the pipeline reaches the codegen pass and prints F# instead of an error list. The 7.9a error-reporter path stays in as a fallback. First time four compiler stages run back-to-back on a shared AST inside a single ll-lang program | ✅ |
| **7.9c** | **Bootstrap compiler reads source from file** — driver now calls `readFile "spec/examples/valid/20a-bootstrap-input.lll"` instead of a hardcoded string literal. New 5-line corpus file holds the clean input module. First concrete step toward `compiler₁ = compile(compiler.lll)` — any .lll file on disk can be the input, which unblocks progressive feature-gap discovery. New file-reading-path regression test (renames input → asserts failure → restores) proves the source flows through `readFile` | ✅ |
| **7.9d** | **Bracket-form types in fn signatures** — `parseParamGroups` / `parseReturnType` now consume `Upper[type][type]...` chains (`Maybe[Int]`, `List[Str]`, `Result[A][E]`) via a new `parseSkipBrackType` helper. Previously, a fn with `Maybe[Int]` return type made the parser desync and silently drop the subsequent `main` decl entirely. New fixture `20b-bootstrap-input-maybe.lll` + regression test prove `wrap(v Int) Maybe[Int] = Some v` now emits both `wrap` and `main` | ✅ |
| **7.9e** | **Stdlib builtins in elaborator env** — `elaborate` now seeds `env0` with `strConcat` / `strLen` / `print` / `printfn` / `listMap` / `listLen` / `listAppend` / `listIsEmpty` / `listFold` / `readFile` via a new `stdlibNames` helper, so fn bodies that call stdlib builtins no longer fire `E002 UnboundVar`. New fixture `20c-bootstrap-input-stdlib.lll` + regression test exercise `strConcat "hi " n` in a fn body | ✅ |
| **7.9f** | **`==` operator in bootstrap compiler** — new two-char `TEqEq` lexer token via a `lexEqOrEqEq` helper, new `EEq Expr Expr` AST variant, new `parseCompare` / `parseCompareTail` precedence layer between `parseExpr` and `parseAddSub` (so comparison binds looser than `+/-`), and a new `EEq` arm threaded through `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` / `emitExpr`. Emits `(<l> = <r>)` from codegen — F# uses a single `=` for equality. New fixture `20d-bootstrap-input-eqeq.lll` + regression test exercise `fn main() Int = if 1 == 1 then 0 else 1`. `<` / `>` / `!=` / `&&` / `\|\|` and full `Bool`-type machinery stay in 7.9g+ | ✅ |
| **7.9g** | **`<` / `>` operators in bootstrap compiler** — new single-char `TLt` / `TGt` lexer tokens (no helper needed; no two-char forms in this slice, and `lexMinusOrArrow` already isolates the `->` case so bare `>` falls through cleanly), new `ELt Expr Expr` / `EGt Expr Expr` AST variants, two new arms in `parseCompareTail` reusing the 7.9f precedence layer unchanged, and `ELt` / `EGt` arms threaded through `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` / `emitExpr`. Emits `(<l> < <r>)` / `(<l> > <r>)` — F# uses `<` / `>` verbatim for integer comparison. New fixture `20e-bootstrap-input-ltgt.lll` + regression test exercise `fn neg(n Int) Int = if n < 0 then 0 - n else n`. Pre-fix was a silent mis-parse: unknown chars dropped, `n < 0` became juxtaposition `(n 0L)`. `<=` / `>=` / `!=` / `&&` / `\|\|` and `Bool`-as-proper-type stay in 7.9h+ | ✅ |
| **7.9h** | **`true` / `false` literals in bootstrap compiler** — two strings (`"true"`, `"false"`) appended to `stdlibNames` in `20-bootstrap-compiler.lll`. The rest of the pipeline was already cleared for boolean literal names: lexer produces `TLower "true"` / `TLower "false"`, `parseAtom` wraps them as `EVar "true"` / `EVar "false"`, `emitExpr`'s `EVar x -> x` pass-through emits them as valid F# boolean literals, and `inferExprType`'s fall-through returns `TyVar "?"` which `typeEq` short-circuits. The only gap was the elaborator's `checkExpr` firing `E002 UnboundVar true` / `E002 UnboundVar false`. Name-scope-only fix; no AST / HM / codegen changes. New fixture `20f-bootstrap-input-bool-lits.lll` + regression test exercise `fn flag() Int = if true == false then 1 else 0`. Proper `Bool` type promotion in HM (so `if 1 then ... else ...` can be rejected) and a dedicated `EBool` AST variant stay in 7.9k+ | ✅ |
| **7.9i** | **Char literals (`'c'`) in bootstrap compiler** — new `TChar Char` lexer token via a `lexCharLit` helper (consume one content char, expect closing `'`, emit `TChar ch`), new `EChar Char` AST variant, new `TChar c :: rest -> (EChar c, rest)` arm in `parseAtom`, and `EChar _` / `EChar c` arms threaded through `isAtomStart` / `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` / `emitExpr`. Emits `'c'` verbatim from codegen — F# accepts single-quoted char literals directly. New fixture `20g-bootstrap-input-char-lit.lll` + regression test exercise `fn sym(n Int) Int = if '=' == '=' then n else 0`. Pivoted from the originally planned "Bool type promotion in HM" slot because char literals are strictly higher impact — the bootstrap compiler's own source uses `'='` / `'"'` / `'('` / `')'` / `'|'` / `'.'` / `':'` / `'_'` dozens of times in `lexChars`, and without char literal support the lexer silently drops `'` and mangles every real ll-lang source file. Escape sequences (`'\n'`, `'\\'`, `'\''`) stay in 7.9j; Bool type promotion moves to 7.9k | ✅ |
| **7.9j** | **Char escape sequences (`'\n'` / `'\t'` / `'\\'` / `'\''`) in bootstrap compiler** — `lexCharLit` extended to detect a leading `\` and dispatch to a new `lexCharEsc` helper that consumes the escape content char, decodes it via `decodeEscape` (n → `'\n'`, t → `'\t'`, `\\` → `'\\'`, `'` → `'\''`, unknown → raw pass-through), and expects the closing quote. Symmetrically, `emitExpr`'s `EChar c` arm now delegates to a new `emitChar` helper that re-escapes the four characters when emitting F# source, so `'\n'` round-trips as valid F# instead of injecting a literal newline into the generated file. The round-trip fix is the key finding of the slice: just extending the lexer wasn't enough — the emitter had to learn symmetric re-escaping at the source-form boundary. New fixture `20h-bootstrap-input-char-esc.lll` + regression test exercise `fn nl(n Int) Int = if '\n' == '\n' then n else 0` alongside a `bs` fn comparing `'\\'`. Needed to lex the bootstrap compiler's OWN source which uses `'\n'` and `'\\'` in `lexChars` — hard blocker for the self-host fixpoint. `\r` / `\0` / `\u....` and string-literal escapes deferred; Bool type promotion stays in 7.9k | ✅ |
| **7.9l** | **Constructor patterns in match arms (bootstrap compiler)** — new `PCon Str List[Pat]` AST variant, new `TUpper name :: rest` arm in `parsePrimaryPat` dispatching to a new `parseCtorArgs` / `parsePatArgs` / `parsePatArgsCons` helper trio that eagerly consumes a sequence of atomic sub-patterns (`TLower _` / `TUnder` / `TInt _` / `TLBrack TRBrack`) until hitting a non-pattern-starter (like `TArrow` / `TBar` / `TColonColon` / `TRParen`). `showPat` / `emitPat` extended with a `PCon name args -> (Name arg1 arg2)` arm (F# DU pattern shape, parens unconditional, space before each arg) via new `showPatArgs` / `emitPatArgs` list walkers. `patBinders` extended with `PCon _ args` recursing through a new `patBindersList`. `patIsCatchAll` extended with `PCon _ _ -> false` (matches only its own tag). `coveredCtors` — previously hard-wired to `[]` with a comment that `Pat` had no `PCon` — now walks the arm pattern list and collects the name from every `PCon`, enabling real exhaustiveness feedback. Pre-fix, `| Some n ->` / `| None ->` silently fell through to the `PWild` catch-all, losing the ctor name and collapsing the match to `let unwrap m = 0L`. New fixture `20i-bootstrap-input-ctor-pat.lll` + regression test exercise `fn unwrap(m Maybe[Int]) Int = match m with \| Some n -> n \| None -> 0`. Single-line match (multi-line match-arm newline-skipping stays a separate concern — `parseArms` still doesn't skip `TNewline`). Direct blocker for self-compiling any lexer / parser / elaborator written in ll-lang itself, every one of which pattern-matches over sum types | ✅ |
| **7.9.newlines** | **Layout-tolerant parsers** — bundles two sibling bugs ([#8](https://github.com/Neftedollar/ll-lang/issues/8) `parseLetIn` + [#12](https://github.com/Neftedollar/ll-lang/issues/12) `parseArms`) into one GREEN commit because the root cause is identical: a parser peeking at the next token without first calling `skipNewlines`. Pre-fix, a multi-line `let x = rhs in\n  body` silently mis-parsed (bug #8 — `parseExpr` on `TNewline :: ...` bottomed out at `parseAtom`'s `EInt 0` default, leaving the body orphaned), and a multi-line `match m with\n  \| Some n -> n\n  \| None -> 0` silently dropped every arm after the first (bug #12 — `parseArms` saw `TNewline :: TBar` and fell through the `TBar :: _` guard into its terminator). `parseLetIn` now skipNewlines after `TEq` and after `TKwIn`. `parseArms` skipNewlines at the top before its `TBar :: _` peek. `parseFnDecl` and `parseLetDecl` skipNewlines after `TEq` — needed because the fn body in the new fixture starts on a fresh line, and without the skip the body parse hit `TNewline` before ever reaching `parseLetIn`. New fixture `20j-bootstrap-input-layout.lll` exercises BOTH paths: `fn describe(m Maybe[Int]) Int =\n  let fallback = 0 in\n  match m with\n    \| Some n -> n\n    \| None -> fallback`. Unblocks multi-line fixtures for every future phase — until now the bootstrap had to cram match bodies and let-in chains onto a single line, a hard blocker for any fixture that exercises real-world layout | ✅ |
| **7.9m** | **String literal escape sequences (`"\n"` / `"\t"` / `"\\"` / `"\""`) in bootstrap compiler** — `takeStrBody` extended to detect `\` inside the string body and dispatch to a new `takeStrBodyEsc` helper that consumes+decodes the next char via `decodeEscape` (reused from 7.9j and extended with a new `"` → `"` arm for the fourth escape). Symmetrically, `emitStr` rewritten to route through new `emitStrBody` + `encodeStrEscape` helpers that walk decoded chars via `listFold` and re-encode each one back to its escaped form — so `"a\nb"` round-trips `source → token → AST → emit` without injecting a raw newline into the generated F# source. Same symmetric-emit gotcha as 7.9j's `emitChar`: just extending the lexer isn't enough, the emitter has to learn to re-escape at the source-form boundary. New fixture `20k-bootstrap-input-str-esc.lll` + regression test exercise `fn greet() Str = "say \"hi\"\n"`, which forces BOTH escape paths — the escaped `\"` would terminate the string prematurely without the lexer fix (produces `E002 UnboundVar hi` pre-fix) and the decoded `\n` would inject a raw newline without the emitter fix. Needed for any bootstrap source that composes F# output via `strConcat` calls with embedded `"\n"` / `" in "` / `"let "` fragments — hard blocker for the self-host fixpoint. `\r` / `\0` / `\u....` / raw strings deferred | ✅ |
| **7.9n** | **`--` line comments in bootstrap compiler** — new top-level `skipLineComment` helper plus one extra arm in `lexMinusOrArrow`: when the char after `-` is also `-`, hand off to `skipLineComment`, which consumes chars up to the next `\n`, emits a `TNewline` to preserve decl-terminator semantics, and resumes `lexChars` on the rest. Discovered via fixpoint probe — pre-fix, running the bootstrap compiler on its own source produced a 465-byte output (module header + prelude only), because a single `--` line comment desynced `parseDecls` and silently dropped every subsequent decl. The bootstrap's own source begins with ~190 lines of `-- header comment` before the first real decl, making this the hardest concrete blocker on the Phase 7.10 fixpoint. New fixture `20l-bootstrap-input-comments.lll` + regression test exercise three comment positions in one file: a full-line comment after the module header, a trailing comment after a decl body, and another full-line comment between decls. Post-fix, the fixpoint probe produces 531 bytes — the prelude plus the `Maybe<'A>` type decl plus the start of `Token`, surfacing the next concrete blocker. Block comments `{- -}` and doc-comment markers (`---`, `|||`) remain deferred | ✅ |
| **7.9o** | **Multi-line type decl layout in bootstrap compiler** — `parseTypeDecl` / `parseCtors` / `parseCtorsTail` in `20-bootstrap-compiler.lll` now tolerate newlines around the constructor list and an optional leading `\|` before the first ctor, so the multi-line form `type Token =\n  \| TKwModule\n  \| TKwType\n  ...` parses cleanly. Pre-fix, `parseTypeDecl` passed `TNewline :: TBar :: ...` straight to `parseCtor`, whose `TUpper _ :: rest` arm never matched and fell through to `MkCtor "?" []` — producing `\| ?` and terminating the ctor list after one bogus entry. Three additive edits: (1) `parseTypeDecl` `skipNewlines` after `TEq`, (2) `parseCtors` strips leading newlines + optional `TBar`, (3) `parseCtorsTail` peeks at `skipNewlines toks` and skips after each `TBar`. The single-line form `type Maybe A = Some A \| None` still parses — fixes are strictly additive. Discovered via fixpoint probe after 7.9n: the bootstrap compiler's own `type Token` (the second real decl in its source) is exactly this multi-line shape. Post-fix probe: the parser now consumes the entire ~1800-line bootstrap source and reaches the elaborator, surfacing `E002 UnboundVar charToInt` as the next layer. New fixture `20m-bootstrap-input-type-layout.lll` + regression test exercise `type Color =\n  \| Red\n  \| Green\n  \| Blue` plus a multi-line match over all three ctors | ✅ |
| **7.9p** | **Stdlib builtins audit in bootstrap compiler** — extended the bootstrap's `stdlibNames` list with `charToInt`, clearing the last `E002 UnboundVar` error in the fixpoint probe. The bootstrap's `elaborate` / `checkDecls` walks every decl and collects every UnboundVar error in one shot, so the probe output is ground truth — the audit needed no regex heuristic. The entire cascade was a single name: `charToInt` (used in `lexChars` / `isUpperChar`). Every other stdlib call (`strConcat`, `strLen`, `printfn`, `listMap`, `listLen`, `listAppend`, `listIsEmpty`, `listFold`, `readFile`, `true`, `false`, `print`) was already in the list. Post-fix probe jumps from 26 bytes (`E002 UnboundVar charToInt`) to **2253 bytes** of emitted F#: module header, prelude, all 13 top-level `type` decls, plus the first fn (`isUpperChar`) — reaching the codegen pass. The codegen pass then halts after one fn with a malformed `if ... then false else 0L` body (mixed Bool/Int branches), which is the next tick's blocker. New fixture `20n-bootstrap-input-stdlib-full.lll` + regression test exercise `fn demo(c Char) Int = charToInt c` — RED before the fix, GREEN after | ✅ |
| **7.9q** | **`parseIf` newline tolerance in bootstrap compiler** — diagnosed 7.9p's "codegen walker halt" as a parser bug, not a codegen bug. The bootstrap's `parseIf` had no `skipNewlines` calls around the `TKwThen` / `TKwElse` match sites, so a multi-line form `if c < 65 then false\n  else if c > 90 then false\n  else true` (exactly what `isUpperChar` uses) tokenised as `... TKwThen false TNewline TKwElse ...` — the `TKwElse :: r` arm missed the leading `TNewline`, fell through to the wildcard, and `parseExpr` on `TNewline :: TKwElse :: ...` bottomed out in `parseAtom`'s `(EInt 0, toks)` wildcard *without consuming*. `parseIf` returned `EIf cond thenE (EInt 0)` with the stray `TKwElse` still on the stream. Back in `parseDecls`, `skipNewlines` ate the newline, head was now `TKwElse`, no arm matched, wildcard `| _ -> []` fired — and every remaining decl was silently dropped. The visible codegen "halt after one fn" was a downstream symptom: only ~14 decls (13 types + 1 fn) reached the codegen pass. Fix: four `skipNewlines` calls in `parseIf`, mirroring the host F# parser's `skipNewlines c` calls at the same sites. Fixpoint probe before/after: 2253 bytes of partial codegen → 86 bytes of `E002 UnboundVar` errors (the *next* blocker, now surfaced because the parser consumes all ~75 decls instead of ~14). Byte count drops but semantic progress is huge — the 2253-byte "partial codegen" was a lie hiding ~60 dropped decls. New fixture `20o-bootstrap-input-multifn.lll` + regression test exercise one multi-line `if-then-else-if-then-else` body followed by four trivial fns; pre-fix only the first fn emits as a singleton `let`, post-fix all five collapse into `let rec one ... and two ... and five ...` | ✅ |
| **7.10a** | **List literal expressions in bootstrap compiler** — new `ENil` / `ECons Expr Expr` AST variants, two new arms in `parseAtom` (`TLBrack :: TRBrack :: rest -> (ENil, rest)`, `TLBrack :: rest -> parseListLit rest`) plus a `parseListLit` helper that reads atoms until `TRBrack` folding right-associatively into `ECons`, a new `TLBrack -> true` arm in `isAtomStart` so `head [1 2 3]` parses as `EApp head [1 2 3]`, and noop / passthrough arms in `checkExpr` / `inferExprType` / `typeCheck` / `showExpr` / `emitExpr` (F# cons syntax `[]` / `(h :: t)` matches ll-lang verbatim). Pre-fix, `parseAtom` had no `TLBrack` arm and every list literal silently fell through to `(EInt 0, toks)` without consuming the `[`, desyncing the token stream. New fixture `20r-bootstrap-input-lists.lll` + regression test exercise `head [1 2 3]` and assert the emitted F# contains `(1L :: (2L :: (3L :: [])))`. Fixpoint probe: 124 → 126 lines (new `ENil` / `ECons` rows in emitted `type Expr`), zero errors. Reveals the next blocker is NOT list literals: `parseArmBody` uses `parseCompare` instead of `parseExpr`, so an `if-then-else` inside a match arm body bottoms out at `parseAtom`'s `EInt 0` wildcard — stays in 7.10b | ✅ |
| **7.10b** | **`if` / `let` in match arm bodies (bootstrap compiler)** — surgical one-function fix to `parseArmBody`: dispatch `TKwIf :: rest -> parseIf rest` and `TKwLet :: rest -> parseLetIn rest` at the top before falling through to `parseCompare toks`. Pre-fix, `parseArmBody` was a straight alias for `parseCompare`, which skips the `parseExpr`-level special-form dispatch — an arm body starting with `if` hit `parseAtom`'s wildcard, was silently replaced by `EInt 0`, and desynced every subsequent decl. The original `parseCompare` constraint was documented as nested-match-ambiguity protection and explicitly scoped to pre-7.5 (no bare `let`/`match`/lambda in arm bodies); the surgical fix relaxes it for `TKwIf` / `TKwLet` only, preserving the nested-`match` guard. Unblocks the `takeIdCont` / `dropIdCont` / `takeDigit` / `dropDigit` lex helper family whose bodies are all `if isIdCont c then listAppend [c] (takeIdCont rest) else []`. New fixture `20s-bootstrap-input-arm-if.lll` + regression test exercise `match n with \| 0 -> if 1 == 1 then 10 else 20 \| _ -> 30`. Fixpoint probe: 2764 bytes of partial codegen (silent garbage, `takeIdCont` body emitted as `-> 0L`) → 153 bytes of a single real elaborator error (`E002 UnboundVar strToInt`). The byte count SHRANK but semantic progress is huge: the parser now produces a well-formed AST for every arm body in the lex helper family, the elaborator finally sees the full program, and `main` correctly takes the `showErrs` branch on an actual undefined name. Next tick 7.10c — add `strToInt` to the bootstrap's stdlib binding set | ✅ |
| **7.10c** | **`strToInt` in bootstrap's `stdlibNames`** — append `"strToInt"` to the bootstrap's flat `stdlibNames` list, mirroring the 7.9p (`charToInt`) / 7.9r (`charIsDigit`) slices. The host elaborator already knows `strToInt` as a builtin; the bootstrap's mirror list was the only gap. Pre-fix, the fixpoint probe halted on `E002 UnboundVar strToInt` after 7.10b's elaborator unblock; post-fix, `main` flips back to the emit branch and output jumps from 153 → 3338 bytes (131 lines, 0 errors). Emission now flows through all 13 type decls and the lex-helper family (`isUpperChar`, `isLowerChar`, `isIdStart`, `isIdCont`, `takeIdCont`, `dropIdCont`, `takeDigit`, `dropDigit`, `parseIntStr`, `classifyIdent`) — the first time bootstrap output has exceeded the pre-7.10a 2726-byte baseline. New fixture `20t-bootstrap-input-strtoint.lll` + regression test exercise `let x = strToInt s in 0` from a user fn body | ✅ |
| **7.10d** | **`PStr Str` string-literal patterns in bootstrap compiler** — new `PStr Str` variant in `type Pat`, plus the six-site cascade through `parsePrimaryPat` (`TStr s :: rest -> (PStr s, rest)` between `TInt` and `TUnder`), `showPat` / `patBinders` / `patIsCatchAll` / `emitPat`. Pre-fix, `parsePrimaryPat` had no `TStr _` arm and every string-literal pattern fell through to the catch-all `| _ -> (PWild, toks)` — silently collapsing arms like `\| "module" -> TKwModule \| "type" -> TKwType ...` into a single `PWild` arm. The 7.10c probe exposed this: `classifyIdent` emitted a one-arm `match s with \| _ -> "module"` instead of the full 14-keyword dispatch. Post-fix, the bootstrap parses all 14 `classifyIdent` arms correctly and the elaborator reaches the previously-unreached wildcard arm body (`let cs = strChars s in ...`), surfacing a new real blocker: `E002 UnboundVar strChars`. Fixpoint probe: 3338 → 1609 bytes (131 → 11 lines); the byte count shrinks because the bootstrap now fails fast at elaboration rather than silently emitting truncated F#. This is forward progress — the 3338-byte "success" was a false positive hiding 13 dropped arms. Next tick 7.10e — add `strChars` to the bootstrap's `stdlibNames` list. New fixture `20u-bootstrap-input-str-pat.lll` + regression test exercise `match s with \| "one" -> 1 \| "two" -> 2 \| _ -> 0` | ✅ |
| **7.10e** | **Small stdlib audit: `strChars` / `strFromChars` / `intToStr` / `charIsSpace` / `listReverse` in bootstrap's `stdlibNames`** — append five missing host builtins to the bootstrap's flat `stdlibNames` list, all referenced from bootstrap fn bodies but never declared as callable names. Same shape as 7.9p (`charToInt`) / 7.9r (`charIsDigit`) / 7.10c (`strToInt`) — the host elaborator's `builtinEnv` already knows all five, but the bootstrap's mirror list was stale. Audit done by probing the bootstrap against itself (5 names surfaced as cascaded `E002 UnboundVar` errors) and cross-referencing Elaborator.fs's builtin table. Fixpoint probe: 1609 → 146 bytes (11 → 5 lines, 2 → 1 errors). The remaining single `E002 UnboundVar s` is a downstream parser desync on a short local name, not a missing stdlib — next phase will trace it. New fixture `20v-bootstrap-input-strchars.lll` + regression test exercise one fn per builtin (`strChars s`, `strFromChars (strChars s)`, `listReverse xs`, `intToStr n`, `charIsSpace c`) and assert the emitted F# contains all five plus `[<EntryPoint>]` | ✅ |
| **7.10f** | **`skipNewlines` in `parseArmBody` before `TKwIf` / `TKwLet` dispatch** — the 7.10e-residual `E002 UnboundVar s` was not a scoping bug. Root cause was parser desync: a multi-line match arm body of the form `\| _ ->\n  let cs = ... in <body>` tokenises as `TBar TUnder TArrow TNewline TKwLet ...`. `parseArmBody` pattern-matched on `TKwLet` / `TKwIf` directly, so the leading `TNewline` fell through to `parseCompare`, which bottomed out at `(EInt 0, toks)` with zero tokens consumed. The arm body became `EInt 0`, the enclosing match returned, and the orphaned `let cs = ... in <body>` was re-parsed by the top-level `parseDecls` loop as a standalone `DLet` — losing the outer fn param scope entirely. In `classifyIdent`, the ejected `<body>` referenced `s` (the outer fn's `(s Str)` param) and produced the phantom `E002 UnboundVar s`. Fix: 15-line diff converting `parseArmBody`'s clause-sugar form into `let toks2 = skipNewlines toks in match toks2 with ...`, mirroring the `skipNewlines` pattern already used by `parseArms`. Fixpoint probe: 146 → 178 bytes, 5 → 6 lines, 1 → 2 errors (`E002 UnboundVar s` gone, replaced by `E002 UnboundVar lexChars` x2 — the next real blocker surfaced by the unblocked parser). No new fixture — existing 23 bootstrap tests stay green. The `s` phantom was masking a separate clause-sugar fn body parser gap, which becomes Phase 7.10g | ✅ |
| **7.10** | **Bootstrap fixpoint** — `compiler₁.fs == compiler₂.fs` (401 lines, 264 bindings, byte-identical) | ✅ |

## Getting Started

Requires [.NET 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/Neftedollar/ll-lang.git
cd ll-lang
dotnet build
dotnet test    # 454 tests
```

### Run your first program

```bash
cat > hello.lll <<'EOF'
module Hello

fn main() = printfn "Hello, ll-lang!"
EOF

dotnet run --project src/LLLangTool -- run hello.lll
# → Hello, ll-lang!
```

### See ll-lang parse itself

```bash
dotnet run --project src/LLLangTool -- run spec/examples/valid/15-moduleparser-real.lll
```

This runs a 979-line ll-lang program that tokenizes, parses, and pretty-prints a whole ll-lang module — written entirely in ll-lang itself.

### CLI

```
lllc build <file.lll>   # compile single file → <file>.fs
lllc build [dir]        # compile project (reads ll.toml) → bin/<name>.fs
lllc run   <file.lll>   # compile and run via dotnet fsi
lllc new   <name>       # scaffold new project
```

### Create a multi-file project

```bash
lllc new myapp          # creates myapp/ll.toml + myapp/src/Main.lll
cd myapp
# edit src/Main.lll, add more .lll files to src/
lllc build              # → bin/myapp.fs + bin/myapp.fsproj
dotnet run --project bin/myapp.fsproj
```

## Problem

LLMs writing code in mainstream languages face two compounding problems: verbose syntax wastes tokens on ceremony rather than logic, and type errors only surface at runtime — after execution, often after damage is done. An LLM generating Python or TypeScript gets no signal that a tagged `UserId` string was passed where an `Email` is expected until the server blows up.

The feedback loop is slow, expensive, and noisy.

## Solution

ll-lang is built around four properties:

- **Token-efficient syntax** — no braces, no semicolons, no boilerplate. Functions, ADTs, and pattern matching in the fewest possible tokens.
- **Static types with inference** — Hindley-Milner type inference. Declare types where they matter, elide them everywhere else.
- **Compiled = works** — tag violations, unbound variables, non-exhaustive matches, and unit mismatches are caught at compile time, not runtime.
- **LLM-readable errors** — all errors follow a compact machine-readable format (`E001 12:5 TypeMismatch ...`) designed for direct consumption by an LLM agent.

## Syntax

### Functions and let bindings

```
module Examples.Basics

let pi = 3.14159

fn add(a Int)(b Int) Int = a + b
fn double(x Int) = x * 2

-- inferred return type
fn square(x Int) = x * x

-- multi-branch if
fn clamp(x Int)(lo Int)(hi Int) Int =
  if x < lo then lo
  else if x > hi then hi
  else x

-- lambda
let triple = \x. x * 3

-- local binding
fn example = let y = double 5 in y + 1
```

### Algebraic Data Types and Pattern Matching

```
module Examples.ADTs

-- product type (record)
type Point = x Float, y Float

-- sum type
type Shape = Circle Float | Rect Float Float | Empty

-- parametric types
type Maybe A = Some A | None
type Result A E = Ok A | Err E

-- exhaustive pattern match
fn area(s Shape) Float =
  | Circle r -> 3.14159 * r * r
  | Rect w h -> w * h
  | Empty    -> 0.0

-- returning Maybe
fn safeDivide(a Float)(b Float) Maybe[Float] =
  if b == 0.0 then None
  else Some (a / b)
```

### Tags, Phantom Types, and Unit Algebra

```
module Examples.Tags

-- declare tags (zero-cost type wrappers)
tag UserId
tag Email

-- tagged value
let uid = "user-42"[UserId]

-- functions reject wrong tags at compile time
fn getUser(id Str[UserId]) Maybe[Str] = Some "alice"
fn sendEmail(to Str[Email]) = to

-- unit algebra: inferred return type Float[m/s]
tag m
tag s

fn speed(d Float[m])(t Float[s]) = d / t

-- phantom types for state machines
tag Validated
tag Raw

type Email[state] = Str

fn validate(s Str) Result[Email[Validated] Str] =
  if s != "" then Ok s
  else Err "empty"
```

## Error Format

All compiler errors are short, structured, and machine-readable — designed so an LLM agent can parse them without extracting from prose:

| Code | Meaning | Example |
|------|---------|---------|
| `E001` | Type mismatch | `E001 12:5 TypeMismatch Str Str[UserId]` |
| `E002` | Unbound variable | `E002 8:3 UnboundVar username` |
| `E003` | Non-exhaustive match | `E003 15:1 NonExhaustiveMatch Shape missing:Empty` |
| `E004` | Unit mismatch | `E004 20:9 UnitMismatch Float[m] Float[s]` |
| `E005` | Tag violation | `E005 7:14 TagViolation Str[Email] Str[UserId]` |

Format: `EXXX line:col ErrorKind details`. No stack traces, no paragraphs, one line per error, parseable by regex.

## Compiler Pipeline

```
Source (.lll)
    ▼  Lexer       — tokenizes with synthetic INDENT/DEDENT
    ▼  Parser      — produces AST
    ▼  Elaborator  — name resolution, tag checks, exhaustiveness
    ▼  HMInfer     — Algorithm W, let-generalization, trait dispatch (E006),
                     occurs check (E008), unit algebra preservation
    ▼  Codegen     — emits idiomatic F# source
    ▼  dotnet fsi  — runs the result (via `lllc run`)
```

## Project Structure

```
spec/                      — formal grammar (EBNF), type rules, example corpus
  grammar.ebnf
  type-system.md
  error-codes.md
  examples/valid/          — working .lll programs (hello, basics, ADTs, ...)
  examples/invalid/        — programs annotated with expected error codes
src/LLLangCompiler/        — compiler library (F#)
  AST.fs                   — untyped surface AST
  Lexer.fs                 — tokenizer with layout (INDENT/DEDENT)
  Parser.fs                — recursive-descent parser
  Elaborator.fs            — name resolution, declared-type checking (E001-E005)
  Types.fs                 — TypeScheme, Subst, generalize/instantiate
  TypedAST.fs              — typed AST after H-M inference
  HMInfer.fs               — Algorithm W, unification (E008), trait dispatch (E006)
  Codegen.fs               — F# source emitter
  Compiler.fs              — end-to-end pipeline entry point
src/LLLangTool/            — `lllc` CLI (build / run)
tests/LLLangTests/         — xUnit test suite (401 tests)
```

## Roadmap

- **Phase 7.6** — elaborator in ll-lang (name resolution shipped in 7.6a; constructor-coverage exhaustiveness shipped in 7.6b; E001 type checking, E004 / E005 tag checks remain), plus heavier front-end slices (multi-line fn bodies, `trait` / `impl` module-level decls).
- **Phase 7.7** — H-M inference rewritten in ll-lang.
- **Phase 7.8** — codegen in ll-lang, then bootstrap fixpoint (compiler₁ == compiler₂).
- **Multi-target backends** — TypeScript / Python / JVM / LLVM after self-hosting lands.

## Design Philosophy

ll-lang is not a general-purpose language. It is optimized for one use case: **LLM agents writing correct code on the first attempt**. Every design decision — significant indentation, juxtaposition-based application, compact error codes, unit algebra — is evaluated against that goal.

Less syntax to generate. More errors caught before execution. Faster iteration loops.

## License

MIT
