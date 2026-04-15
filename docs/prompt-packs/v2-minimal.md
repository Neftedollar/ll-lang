## ll-lang context (v2)

ll-lang is a statically-typed functional language for LLM code generation.
15 keywords. No `fn`, `type`, `in`, `then`, `with`. HM type inference.
File extension: `.lll`. Every file starts with `module Path.Name`.

## Syntax quick reference

```
-- Types: Uppercase, no 'type' keyword
Maybe A = Some A | None
Shape = Circle Float | Rect Float Float | Empty

-- Functions: lowercase + parens, no 'fn' keyword
add(a Int)(b Int) = a + b
area(s Shape) =         -- match on last param via | arms
  | Circle r -> 3.14 * r * r
  | Rect w h -> w * h
  | Empty -> 0.0

-- If: no 'then'; body indented
f(x Int) =
  if x > 0
    x
  else 0

-- Match: no 'with'
g(m Maybe[Int]) =
  match m
    | Some n -> n
    | None -> 0

-- Local bindings: chain via layout, no 'let-in'
h(x Int) =
  y = x * 2
  y + 1

-- Lambda
double = \x. x * 2

-- Tag (zero-cost newtype)
tag UserId
uid = "user-42"[UserId]

-- Side effects
main =
  _ = printfn "hello"
  0
```

## MCP tools

Use `lllc mcp` for structured feedback: `check_source`, `lookup_error`, `stdlib_search`, `grammar_lookup`.

## Error codes

E001 TypeMismatch · E002 UnboundVar · E003 NonExhaustiveMatch ·
E004 UnitMismatch · E005 TagViolation · E008 InfiniteType
