# Modules

Every `.lll` file starts with a module header. Modules give types, functions,
and impls a namespace and will eventually be the unit of separate compilation.

## Module header

```lll
module Examples.Basics
```

The header must be the first non-comment line. The path is one or more
uppercase-starting segments joined by dots.

Conventionally the path mirrors the directory layout, e.g. a file at
`spec/examples/valid/03-tags.lll` declares `module Examples.Tags`.

## Imports

```lll
module Examples.Modules

import Std.List
import Std.Maybe

fn firstDoubled(xs List[Int]) Maybe[Int] =
  xs -> head -> map (\x. x * 2)
```

Each `import` declares a dependency on another module. Imports appear
immediately after the module header, before any declarations.

## Exports

```lll
export fn greet(name Str) Str = "hello " + name
```

Prefixing a declaration with `export` marks it as visible to other modules.
Without `export`, a declaration is module-private.

The current compiler parses `export` and stores the flag in the AST
(`LLModule.Decls : (Decl * bool) list`), but since cross-module linking is
not yet wired, every declaration is effectively visible within its own file.

## Codegen

A `module Examples.Basics` header emits an F# module header:

```fsharp
module Examples.Basics
```

All top-level decls in the ll-lang file end up as top-level F# `let`
bindings and `type` declarations inside that module.

## Known limitations

The module system is parsed and tracked through the typed AST, but several
pieces are not yet implemented:

- **No standard library**. `import Std.List`, `import Std.Maybe`, etc. parse
  without error, but the referenced names (`head`, `map`, `fromMaybe`, ...)
  are not in scope and will produce `E002 UnboundVar` if used. Corpus file
  `05-modules.lll` parses but uses names that would fail inference at runtime.
- **No multi-file compilation**. Each invocation of `llc build` or `llc run`
  operates on a single `.lll` file. Cross-file symbol resolution is a
  planned Phase 6 feature.
- **No `Platform.*` modules**. The design spec reserves `Platform.IO`,
  `Platform.DotNet.ASP`, etc. None are implemented; `E007 PlatformMismatch`
  is reserved in the error table but never emitted.
- **No `export` visibility enforcement**. The flag is tracked but every
  in-file declaration is accessible.

## Practical advice

Until the module system is fleshed out:

- Put everything you need in a single `.lll` file.
- Define helpers locally rather than relying on `Std.*`.
- Only `printfn` is available as a pre-declared IO builtin (see the
  `fn main()` example in [01-installation](01-installation.md)).

A self-contained file is the safest shape to ship today.
