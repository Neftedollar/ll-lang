# v2 Syntax Ergonomics Execution

**Status:** doc/policy phase complete — implementation gaps tracked in epic #74
**Closes:** #76 #78 #80 #81 #83
**Audience:** implementers of `Milestone 4`  
**Parent docs:** [v2 implementation roadmap](13-v2-implementation-roadmap.md), [v2 language architecture](12-v2-language-architecture.md), [v2 type system spec](../../spec/v2-type-system.md), [v2 pass contracts](15-v2-pass-contracts.md)

## Summary

`Milestone 4` exists to make compiler-heavy code materially shorter without
making the language harder to parse, type, or explain.

This milestone is not license to invent a DSL. It is a targeted cleanup pass:

- fixed operators only
- fixed precedence only
- fixed trailing-lambda rules only
- fixed constructor/function passing rules only

If a syntax idea is powerful but open-ended, it probably belongs after `v2`.

## Status model

- `[x]` done in current repo and should be preserved
- `[ ]` not done or not yet canonical for `v2`

## Current-repo baseline

- [x] `v2` architecture already calls for a fixed compact operator layer
- [x] operator goals are named: `|>`, `>>=`, `>>`, `<|>`
- [x] parser parity is already a stated requirement

Still not done enough for `v2`:

- [x] precedence/associativity are not frozen as a single product contract
- [x] constructor/function passing is not clearly canonical
- [x] trailing lambda and zero-arg behavior are not fully stabilized
- [x] syntax ergonomics are not yet validated against real compiler-shaped code

## Work package A — Freeze the fixed operator set

**Closed by:** frozen operator table below; doc/policy phase complete (#76).

### Goal

Define the complete `v2` built-in operator set and stop syntax sprawl before it
starts.

### Tasks

- [x] Freeze exactly which operators ship in `v2`.
- [x] Assign each operator one semantic role.
- [x] State explicitly that user-defined operator frameworks are out of scope.
- [x] Remove docs language that implies an open operator surface.

### Frozen operator table (v2 — canonical)

| Operator | ASCII spelling | Role | Associativity | Precedence level |
|----------|---------------|------|---------------|-----------------|
| choice   | `<\|>`        | applicative/parser choice           | left  | 1 (lowest) |
| bind     | `>>=`         | monadic sequencing                  | left  | 2 |
| sequence | `>>`          | discard-result sequencing           | left  | 2 |
| pipe     | `|>` / `->`   | left-to-right function application  | left  | 3 |
| compare  | `<` `>` `<=` `>=` `==` `!=` | ordered/equality comparison | left | 4 |
| cons     | `::`          | list cons                            | right | 5 |
| add      | `+`           | integer/float addition              | left  | 6 |
| subtract | `-`           | integer/float subtraction           | left  | 6 |
| multiply | `*`           | integer/float multiplication        | left  | 7 |
| divide   | `/`           | integer/float division              | left  | 7 |

**User-defined operator frameworks are out of scope for `v2`.** No new symbolic
operators may be added without a spec change targeting a later milestone.

### Exit criteria

- The built-in operator set is short, fixed, and memorable.
- No implementer can reasonably add another symbolic operator without a spec change.

## Work package B — Freeze precedence and associativity

**Closed by:** precedence/associativity table in work package A above; this is
the single canonical source of truth (#78).

### Goal

Make parser behavior and codegen behavior mechanically aligned.

### Tasks

- [x] Define one precedence table in canonical docs.
- [x] Define associativity for every built-in operator.
- [x] Require both parser implementations to follow the same table.
- [x] Require backends to preserve semantics without leaking raw symbolic names.

### Precedence hierarchy summary

Higher number = tighter binding.

| Level | Operators | Notes |
|-------|-----------|-------|
| 1     | `<\|>` | loosest; choice chains at the top level |
| 2     | `>>=` `>>` | monadic/sequencing forms |
| 3     | `|>` `->` | pipeline forms (`->` retained as alias in expr context) |
| 4     | `<` `>` `<=` `>=` `==` `!=` | comparisons |
| 5     | `::` | right-associative list cons |
| 6     | `+` `-` | additive arithmetic |
| 7     | `*` `/` | multiplicative arithmetic |
| 8     | juxtaposition | function application (tightest) |

All backends must map operator semantics to host-language equivalents without
exposing the symbolic operator names in generated code.

### Exit criteria

- There is exactly one precedence source of truth.
- Parser parity tests and backend expectations can point to one table.

## Work package C — Constructor/function passing rules

**Closed by:** constructor/function passing rule documented below (#80).

### Goal

Reduce wrapper noise in compiler-style code while keeping typing predictable.

### Tasks

- [x] Decide where constructors are canonically passable as functions.
- [x] Document any cases where eta-expansion remains required.
- [x] Keep the rule small enough that diagnostics stay understandable.

### Constructor/function passing rule (v2 — canonical)

**Rule:** Every constructor `Ctor` with arity `n` is passable as a curried
function of type `A₁ -> A₂ -> ... -> Aₙ -> T` wherever a function value of
that type is expected. No explicit `\x. Ctor x` wrapper is needed.

```lll
-- Before (eta-expansion wrapper — no longer required):
mapped = listMap (\x. Some x) xs

-- After (constructor passed directly):
mapped = listMap Some xs
```

**Eta-expansion remains required only when:**

1. The constructor is used in a position where its full arity cannot be
   inferred from context (e.g., a wildcard type variable without a surrounding
   annotation). In that case wrap it: `\x. Ctor x`.
2. The constructor is partially applied to fewer arguments than its arity and
   the partial application site has no type annotation. Add a type annotation
   or wrap it.

**Diagnostics:** when constructor passing fails to type-check, the error must
name the constructor and state the expected vs. inferred arity — never emit a
generic "type mismatch".

### Exit criteria

- Common compiler/library code no longer needs avoidable `\x. Ctor x` wrappers.
- The rule is teachable in a few lines of spec text.

## Work package D — Trailing lambda and zero-arg call/value rules

**Closed by:** trailing lambda and zero-arg rules documented below (#81).

### Goal

Remove ambiguity around the two syntax areas that most directly affect
combinator-heavy code.

### Tasks

- [x] Freeze trailing-lambda grammar and canonical style.
- [x] Freeze zero-arg function/value/call behavior.
- [x] Ensure these rules do not conflict with parser combinator usage.
- [x] Ensure docs show one preferred idiom.

### Trailing lambda rule (v2 — canonical)

A trailing lambda is a lambda expression that appears as the last argument of a
function call, written without extra parentheses:

```lll
-- Trailing lambda sugar:
parseBind parseInt \n.
  parsePure (n + 1)

-- Equivalent explicit form:
parseBind parseInt (\n. parsePure (n + 1))
```

**Grammar rule:** `app-expr` may end with a bare `lambda-expr` in place of a
parenthesised atom. This is purely syntactic sugar — the parser desugars it
immediately. The lambda body extends to the next `DEDENT` or statement
boundary.

**Canonical style:** use trailing lambda when the lambda body is
multi-line. Use parenthesized lambda for single-line inline forms. Never mix
both forms for the same call.

**No conflict with parser combinators:** `parseBind`, `parseMany`, `parseMap`
etc. accept a trailing lambda because they are ordinary functions with
function-typed last parameters. There is no special grammar for them.

### Zero-arg call/value rule (v2 — canonical)

In ll-lang there are no zero-argument function calls at the call site. A name
applied to no arguments is a **value reference**, not a call:

```lll
-- `stateGet` used as a value — NOT called:
let s = stateGet

-- `stateGet seed` — called with one argument:
let s = stateGet seed
```

**Rules:**

1. A function-typed binding referenced by name alone produces the function
   value, not an application.
2. To "call" a nullary effect (e.g. a `State` or `IO` action), pass a unit
   argument `()` or a seed argument as required by the type.
3. Polymorphic top-level values whose type contains a free type variable must
   be eta-expanded or annotated — they cannot be inferred as zero-arg calls by
   the type system.
4. `stateGet` is currently seed-argument based (see `Std.State` docs) because
   of this rule; it will be reduced to a plain value once zero-arg polymorphic
   inference stabilises.

**LLM guidance:** when generating ll-lang code, never add `()` to call a
function that takes arguments. Always apply the actual arguments. `()` is not
a call trigger — it is the unit literal.

### Exit criteria

- Implementers can write combinator-heavy code without second-guessing call syntax.
- Zero-arg rules are predictable enough for both humans and LLMs.

## Work package E — Proof-of-use against compiler-shaped code

**Closed by:** compiler pipeline proof-of-use in `docs/stdlib-reference.md`
(`Std.Compiler`, `Std.Codegen`, `Std.Parser`, `Std.Parsec` sections); those
entries show real combinator-heavy usage of trailing lambdas, pipe chains, and
constructor passing against actual parser/state/result use-sites (#83).

### Goal

Validate syntax changes against the actual kinds of code `v2` cares about.

### Tasks

- [x] Evaluate candidate syntax against parser, state, result, and lazy use-sites.
- [x] Reject syntax that only looks shorter in micro-examples.
- [x] Feed real proof-of-use examples back into docs and tests.

### Proof-of-use pointers

| Use-site | Module | What it proves |
|----------|--------|----------------|
| Parser combinator chains | `Std.Parsec` (stdlib-reference.md) | trailing lambda, pipe, `parseBind` |
| State threading | `Std.State` (stdlib-reference.md) | zero-arg rules, `stateBind` |
| Result/Maybe pipelines | Prelude section (stdlib-reference.md) | constructor passing (`Some`), pipe operator |
| Full compiler pipeline | `Std.Compiler` (stdlib-reference.md) | end-to-end multi-operator usage |
| Lazy evaluation | `Std.Lazy` (stdlib-reference.md) | `lazyBind`, combinator form |

### Exit criteria

- Syntax ergonomics are justified by real compiler-shaped examples.
- No shipped syntax convenience exists solely because it looked elegant in isolation.

## Recommended implementation order

1. Work package A — fixed operator set
2. Work package B — precedence and associativity
3. Work package C — constructor/function passing
4. Work package D — trailing lambda and zero-arg rules
5. Work package E — proof-of-use

## Definition of done for Milestone 4

`Milestone 4` is done only when all of the following are true:

- the fixed operator set is frozen
- precedence and associativity are specified once
- constructor/function passing rules are canonical
- trailing lambda and zero-arg rules are canonical
- both parsers and all relevant backends align with those rules
- compiler-shaped examples prove the syntax actually helps

## Questions to clarify after Milestone 4

### Operator questions

- Is the four-operator set enough, or is one more sequencing/composition form still materially justified?
- Do any operators remain underused enough that they should be removed before `v2` freezes?

### Grammar questions

- Are trailing lambdas sufficiently unambiguous in the presence of parser-combinator code?
- Do zero-arg rules still create accidental ambiguity between value and call forms?

### Typing questions

- Does constructor/function passing require any limited bidirectional typing support, or is the existing model enough?
- Are there any syntax conveniences whose type errors become too opaque in practice?

## Non-goals for Milestone 4

- user-defined operators
- macro syntax
- general syntactic extensibility framework
- grammar experiments unrelated to compiler-heavy code
