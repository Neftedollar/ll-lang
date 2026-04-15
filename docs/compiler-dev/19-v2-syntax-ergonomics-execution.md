# v2 Syntax Ergonomics Execution

**Status:** active planning document  
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

- [ ] precedence/associativity are not frozen as a single product contract
- [ ] constructor/function passing is not clearly canonical
- [ ] trailing lambda and zero-arg behavior are not fully stabilized
- [ ] syntax ergonomics are not yet validated against real compiler-shaped code

## Work package A — Freeze the fixed operator set

### Goal

Define the complete `v2` built-in operator set and stop syntax sprawl before it
starts.

### Tasks

- [ ] Freeze exactly which operators ship in `v2`.
- [ ] Assign each operator one semantic role.
- [ ] State explicitly that user-defined operator frameworks are out of scope.
- [ ] Remove docs language that implies an open operator surface.

### Exit criteria

- The built-in operator set is short, fixed, and memorable.
- No implementer can reasonably add another symbolic operator without a spec change.

## Work package B — Freeze precedence and associativity

### Goal

Make parser behavior and codegen behavior mechanically aligned.

### Tasks

- [ ] Define one precedence table in canonical docs.
- [ ] Define associativity for every built-in operator.
- [ ] Require both parser implementations to follow the same table.
- [ ] Require backends to preserve semantics without leaking raw symbolic names.

### Exit criteria

- There is exactly one precedence source of truth.
- Parser parity tests and backend expectations can point to one table.

## Work package C — Constructor/function passing rules

### Goal

Reduce wrapper noise in compiler-style code while keeping typing predictable.

### Tasks

- [ ] Decide where constructors are canonically passable as functions.
- [ ] Document any cases where eta-expansion remains required.
- [ ] Keep the rule small enough that diagnostics stay understandable.

### Exit criteria

- Common compiler/library code no longer needs avoidable `\\x. Ctor x` wrappers.
- The rule is teachable in a few lines of spec text.

## Work package D — Trailing lambda and zero-arg call/value rules

### Goal

Remove ambiguity around the two syntax areas that most directly affect
combinator-heavy code.

### Tasks

- [ ] Freeze trailing-lambda grammar and canonical style.
- [ ] Freeze zero-arg function/value/call behavior.
- [ ] Ensure these rules do not conflict with parser combinator usage.
- [ ] Ensure docs show one preferred idiom.

### Exit criteria

- Implementers can write combinator-heavy code without second-guessing call syntax.
- Zero-arg rules are predictable enough for both humans and LLMs.

## Work package E — Proof-of-use against compiler-shaped code

### Goal

Validate syntax changes against the actual kinds of code `v2` cares about.

### Tasks

- [ ] Evaluate candidate syntax against parser, state, result, and lazy use-sites.
- [ ] Reject syntax that only looks shorter in micro-examples.
- [ ] Feed real proof-of-use examples back into docs and tests.

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
