---
name: Bug report
about: Something the compiler does wrong — wrong output, crash, or incorrect error
title: "[bug] "
labels: bug
assignees: ''
---

## ll-lang source

Paste the smallest `.lll` program that reproduces the issue:

```
module Repro

-- your code here
```

## Expected behaviour

What should happen (correct output, accepted by the compiler, etc.):

```
-- expected output or "should compile without errors"
```

## Actual behaviour

What actually happens — paste the full compiler output including error codes:

```
-- paste lllc output here
```

## Environment

| | |
|-|--|
| `lllc` version / commit | e.g. `1.0.0` or `git rev-parse HEAD` output |
| .NET version | `dotnet --version` output |
| Target | e.g. `fs` (default), `ts`, `py`, `java`, `cs`, `llvm` |
| OS | e.g. macOS 14, Ubuntu 22.04, Windows 11 |

## Steps to reproduce

1. Create the file above as `repro.lll`
2. Run `lllc build repro.lll` (or `lllc run repro.lll`)
3. See error

## Additional context

Anything else that might help: related error codes, a link to the spec section you expected to apply, etc.
