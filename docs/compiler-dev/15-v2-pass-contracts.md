# v2 Pass Contracts

**Status:** planning aid for Milestone 1  
**Audience:** implementation agents and maintainers  
**Purpose:** make compiler phase boundaries decision-complete before large-scale self-hosted migration work begins.

## Summary

This document defines the required pass contracts for the canonical `v2`
compiler. It is intentionally operational: each pass has one job, one input
shape, one output shape, and explicit non-goals. The objective is to stop
backend emitters and tooling layers from re-deriving semantics ad hoc.

## Global rules

Every major pass must satisfy all of the following:

- one canonical owner module group
- one explicit input contract
- one explicit output contract
- validation tests at the pass boundary
- documentation updates in the same change when the contract changes

No pass is allowed to silently absorb responsibilities that belong to another
phase just because the current implementation makes that convenient.

## Pass graph

The canonical `v2` graph is:

1. `Compiler.Syntax.Lexer`
2. `Compiler.Syntax.Parser`
3. `Compiler.Frontend.Elaborator`
4. `Compiler.Types`
5. `Compiler.Typed`
6. `Compiler.Infer`
7. `Compiler.Lower`
8. `Compiler.Backend.<Target>`
9. `Compiler.Project.Manifest`
10. `Compiler.Project.Loader`
11. `Compiler.Cli`

Project loading and CLI orchestration wrap the language pipeline but still need
their own contracts because they determine how the compiler is used in
practice.

## Phase contracts

### 1. `Compiler.Syntax.Lexer`

**Input:** raw source text  
**Output:** token stream with source positions and layout tokens  
**Must own:**

- lexical classification
- comment stripping
- layout token synthesis
- literal tokenization
- source position tracking at token granularity

**Must not own:**

- declaration parsing
- precedence
- type checking
- backend-specific behavior

### 2. `Compiler.Syntax.Parser`

**Input:** token stream  
**Output:** surface AST plus position map or equivalent source-location mapping  
**Must own:**

- declaration parsing
- expression parsing
- precedence and associativity
- pattern parsing
- syntactic sugar expansion only when it is purely syntactic

**Must not own:**

- name resolution
- type inference
- target-specific rewriting

### 3. `Compiler.Frontend.Elaborator`

**Input:** surface AST  
**Output:** validated surface AST plus symbol/declaration environment and
frontend diagnostics  
**Must own:**

- name resolution
- declared-type consistency checks
- constructor/binder environment construction
- exhaustiveness and frontend structural checks

**Must not own:**

- general HM inference
- backend lowering
- project graph logic

### 4. `Compiler.Types`

**Input:** type-level declarations and frontend requirements  
**Output:** canonical type representations, substitutions, schemes, utility
operations  
**Must own:**

- type representation
- substitution machinery
- free-variable calculations
- generalization/instantiation support

**Must not own:**

- AST walking
- project loading
- backend emission

### 5. `Compiler.Typed`

**Input:** typed-core expression and declaration shapes required by inference and
lowering  
**Output:** canonical typed IR definitions  
**Must own:**

- typed expression forms
- typed declaration forms
- expression IDs or equivalent stable identity hooks
- metadata needed by lowering/backends

**Must not own:**

- inference algorithm
- backend rendering

### 6. `Compiler.Infer`

**Input:** elaborated surface AST and type environment  
**Output:** typed IR plus inference diagnostics and evidence tables where
needed  
**Must own:**

- HM inference / unification
- typed-core construction
- operator typing enforcement
- principal-type behavior where intended

**Must not own:**

- match lowering
- backend-specific representations
- project-system concerns

### 7. `Compiler.Lower`

**Input:** typed IR  
**Output:** backend-neutral lowered IR  
**Must own:**

- explicit lowering of pattern matching
- canonicalization of syntactic conveniences
- preparation for backend emission

**Must not own:**

- source parsing
- environment construction
- target-specific pretty-printing

### 8. `Compiler.Backend.<Target>`

**Input:** lowered IR  
**Output:** target artifact text or target-specific intermediate form  
**Must own:**

- target syntax rendering
- target runtime/helper mapping
- target naming/stability conventions

**Must not own:**

- re-typechecking source constructs
- redoing language-level lowering decisions that should already be explicit in
  `Compiler.Lower`

### 9. `Compiler.Project.Manifest`

**Input:** manifest text  
**Output:** canonical manifest model  
**Must own:**

- manifest parsing
- manifest validation
- dependency declaration decoding

**Must not own:**

- filesystem graph walking
- backend codegen

### 10. `Compiler.Project.Loader`

**Input:** project root + manifest model  
**Output:** resolved project graph in deterministic load/build order  
**Must own:**

- source discovery
- import-to-file resolution
- dependency graph construction
- cycle detection
- topo ordering

**Must not own:**

- type inference
- backend emission
- CLI UX policy

### 11. `Compiler.Cli`

**Input:** user command line / MCP request / tool invocation  
**Output:** orchestrated compiler action and stable diagnostics/artifacts  
**Must own:**

- command dispatch
- entrypoint selection
- command-mode policy (`build`, `check`, `run`, `install`, `mod *`, MCP)
- user-facing output shaping

**Must not own:**

- language semantics
- internal backend logic
- ad hoc parsing/typechecking shortcuts outside the canonical pipeline

## Required migration discipline

When moving a subsystem from stage0 to canonical ll-lang ownership:

1. define the owner module group
2. define the input/output contract
3. identify current stage0-only assumptions
4. add or update direct tests for the ll-lang-owned path
5. update all docs that name the old owner

If any step is missing, the migration is incomplete.

## Validation targets

Milestone 1 is not done until:

- each phase above has one documented owner
- docs consistently reference the same phase graph
- tests exist at the main pass boundaries
- no major subsystem is “canonical by implication only”
