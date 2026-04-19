# ll-lang Error Codes

Errors are emitted in two formats:

**Compact (default):**  `EXXX line:col ErrorName details hint:fix`
**Human (--human):**    multi-line with explanation

## Codes

### E001 TypeMismatch
Expected type A, got type B at a usage site.

Compact: `E001 12:5 TypeMismatch expected:UserId got:Int hint:wrap:UserId`

### E002 UnboundVar
Identifier not found in scope.

Compact: `E002 5:3 UnboundVar name:foo`

### E003 NonExhaustiveMatch
Pattern match does not cover all constructors of a sum type.

Compact: `E003 8:1 NonExhaustiveMatch type:Shape missing:Empty`

### E004 UnitMismatch
Incompatible units in arithmetic or argument position.

Compact: `E004 15:10 UnitMismatch expected:Float[m] got:Float[kg]`

### E005 TagViolation
Untagged value passed where tagged value expected.

Compact: `E005 7:8 TagViolation expected:Str[UserId] got:Str`

### E007 PlatformMismatch
A platform-specific module imported but the compile target doesn't support it.

Compact: `E007 2:1 PlatformMismatch module:Platform.DotNet.ASP target:python`

### E008 InfiniteType
Type unification would produce an infinite type (occurs-check failure).

Compact: `E008 3:1 InfiniteType var:a cycle:a=List[a]`

### E020 ModulePathMismatch
The `module` declaration in a file does not match the file's location in the project tree.

Compact: `E020 0:0 ModulePathMismatch file:src/Foo/bar.lll expected:Foo.Bar got:Foo.Baz`

Repair: rename the file or fix the `module X.Y` declaration so they agree.

### E024 ModuleCycle
A circular dependency was detected in the module import graph.

Compact: `E024 0:0 ModuleCycle Foo -> Bar -> Foo`

Repair: break the cycle — extract shared definitions into a third module that both can import.

### E025 NoProjectForImport
A non-`Std.*` import was used in single-file mode (no `lll.toml` project file found).

Compact: `E025 0:0 NoProjectForImport import:App.Utils`

Repair: either run in project mode (`lllc check [dir]` / `lllc build [dir]`) or use the canonical self-hosted single-file checker (`lllc self check <file>`).

### E026 UnknownExternalMapping
An `external` declaration has no mapping for the selected compile target.

Compact: `E026 5:1 UnknownExternalMapping target:typescript name:httpGet`

Repair: add the function to the target's Platform SDK or provide a companion implementation file.

## Invalid example convention

Every file in `spec/examples/invalid/` declares its expected error on line 1:

```
-- expect: E001
```

The test runner asserts the compiler emits exactly the declared error code.
