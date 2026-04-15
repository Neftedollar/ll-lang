# Adding a new error code

End-to-end walkthrough for introducing a hypothetical `E009 ShadowedBinding`
error. Substitute your own code and rule as needed — the steps are identical.

## 1. Reserve the code in the spec

Edit `spec/error-codes.md` and add the new entry to the catalog,
keeping the format consistent with E001-E008:

```markdown
### E009 ShadowedBinding
A `let` introduces a name already bound in an enclosing scope.

Compact: `E009 12:5 ShadowedBinding name:x`
```

## 2. Add the case to `LLLang.Elaborator.ErrorCode`

Edit `src/LLLangCompiler/Elaborator.fs`:

```fsharp
type ErrorCode = E001 | E002 | E003 | E004 | E005 | E006 | E008 | E009
```

The discriminated union case is the canonical machine name. Codegen
modules and tests reference `E009` directly.

## 3. Add a constructor helper

In the same file, near the existing `e001`/`e002`/... helpers:

```fsharp
let private e009 line col name = {
    Code = E009; Line = line; Col = col
    Message = sprintf "E009 %d:%d ShadowedBinding %s" line col name }
```

Keep the message format aligned with the rest: `EXXX line:col Name details`.
This is what an LLM agent will pattern-match.

## 4. Detect the violation

The detection lives wherever the relevant condition can first be
observed. For `E009 ShadowedBinding`, the natural place is the
elaborator's `typeOf` or a new dedicated pass:

```fsharp
| ELet(x, e, Some body) ->
    let (eTy, eErrs) = typeOf e env
    let shadowErr =
        if Map.containsKey x env then [e009 0 0 x]
        else []
    let env' = Map.add x eTy env
    let (bTy, bErrs) = typeOf body env'
    (bTy, eErrs @ shadowErr @ bErrs)
```

For an error that should fire during inference instead, add it inside
`HMInfer.fs` and use the `mkErr` / `e00X` helpers there:

```fsharp
let private e009 name = mkErr E009 $"E009 ShadowedBinding {name}"
```

Then push to `st.Errors` at the relevant inference site.

## 5. Add an invalid corpus example

Create `spec/examples/invalid/E009-shadow.lll`:

```lll
-- expect: E009
module Invalid.E009

f =
  x = 1
  x = 2
  x
```

Line 1 must be `-- expect: E009` exactly — the corpus runner pattern-
matches that prefix.

## 6. Add a positive test

Open `tests/LLLangTests/ElaboratorTests.fs` (or `HMInferTests.fs` if
you wired it there) and add:

```fsharp
[<Fact>]
let ``E009 fires on let-shadowing`` () =
    let src = readInvalid "E009-shadow.lll"
    let errs = inferErrs src
    Assert.Contains(errs, fun e -> e.Code = E009)
```

Run:

```bash
dotnet test --filter "E009"
```

## 7. Update documentation

Two doc tracks need touching:

- `docs/user-guide/07-error-codes.md` — add a new section with the
  reproduction snippet, the compact output format, and a fix recipe.
- `docs/compiler-dev/05-hm-inference.md` or `04-elaborator.md` —
  document where the check lives and any new helpers.

The `README.md` error table at the top of the repo also lists the
error codes; keep it in sync with the spec.

## 8. Verify nothing else broke

```bash
dotnet test
```

The whole suite should pass (see CI for current count), plus whatever
positive test you added. If a previously-valid corpus file now
triggers your new error, decide:

- The new error is correct and the corpus example was always wrong →
  fix the corpus.
- The new error is too aggressive → tighten the detection rule.

## Checklist

```
[ ] spec/error-codes.md             — entry added
[ ] Elaborator.fs                   — case added to ErrorCode union
[ ] Elaborator.fs (or HMInfer.fs)   — eXXX helper + detection logic
[ ] spec/examples/invalid/EXXX-*.lll — minimal trigger
[ ] tests/LLLangTests/*Tests.fs     — positive test
[ ] docs/user-guide/07-error-codes.md — user-facing entry
[ ] docs/compiler-dev/0X-*.md       — pass-level doc updated if relevant
[ ] README.md                       — top-level error table updated
[ ] dotnet test                     — full suite green
```

## Conventions to follow

- **Compact format only.** The compact form is the contract; if you
  add a `--human` formatter later, derive it from the structured
  fields.
- **No prose in `LLError.Message`.** It must be parseable by a regex
  like `EXXX (\d+):(\d+) (\w+) (.*)`.
- **Errors collect, don't throw.** Push to `st.Errors` (HM) or return
  in the error list (Elaborator). The pipeline never throws on a
  user-program error.
- **Position info is threaded via `PosMap`.** The parser records a
  `Pos` on every source-bearing node and both the elaborator and
  HMInfer look positions up via `posOf pm node`. New errors should
  carry positions the same way; fall back to `0:0` only for nodes
  the compiler itself synthesised (and therefore never had a source
  location).
