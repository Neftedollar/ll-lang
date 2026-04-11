# Tutorial 02: Types and Pattern Matching

Algebraic data types are the core of ll-lang. This tutorial covers sum types, exhaustive matching, `Maybe`/`Result`, and shows the token savings vs TypeScript and Python.

## Defining a sum type

```lll
module Shapes

Shape = Circle Float | Rect Float Float | Empty
```

`Shape` is a sum type with three constructors:
- `Circle` wraps one `Float` (the radius)
- `Rect` wraps two `Float`s (width, height)
- `Empty` carries no data

No `type` keyword, no angle brackets, no `|` on the first constructor. Uppercase name → type declaration.

## Pattern matching

```lll
area(s Shape) Float =
  match s
    | Circle r     -> 3.14159 * r * r
    | Rect w h     -> w * h
    | Empty        -> 0.0
```

The compiler checks exhaustiveness. If you remove the `Empty` arm:

```
E003 4:1 NonExhaustiveMatch type:Shape missing:Empty
```

One line, machine-readable, no stack trace. Add the arm, recompile, done.

## Clause sugar — shorter match syntax

When a function body consists entirely of match arms, you can drop `match s`:

```lll
area(s Shape) Float =
  | Circle r  -> 3.14159 * r * r
  | Rect w h  -> w * h
  | Empty     -> 0.0
```

The compiler implicitly matches on the last parameter (`s`). Same semantics, fewer tokens.

## Maybe and safe operations

ll-lang has no `null`. Return `Maybe A` instead:

```lll
safeDivide(a Float)(b Float) =
  if b == 0.0
    None
  else Some (a / b)
```

Callers must handle both cases:

```lll
showDivision(a Float)(b Float) =
  match safeDivide a b
    | Some result -> strConcat "Result: " (floatToStr result)
    | None        -> "Division by zero"
```

The compiler enforces this — you cannot pass `Maybe[Float]` where `Float` is expected (E001).

## Result for errors with detail

```lll
ParseError = EmptyInput | InvalidChar Char | Overflow

parseAge(s Str) =
  if s == ""
    Err EmptyInput
  else
    match strToInt s
      | None   -> Err (InvalidChar 'x')
      | Some n ->
        if n < 0 || n > 150
          Err Overflow
        else Ok n
```

`Result A E = Ok A | Err E` is in the prelude. Chain results with `resultBind`:

```lll
validateUser(ageStr Str)(nameStr Str) =
  resultBind parseAge ageStr (\age.
    if strLen nameStr == 0
      Err EmptyInput
    else Ok (nameStr, age))
```

## Parametric types

Types can take type parameters:

```lll
Pair A B = MkPair A B

fst(p Pair[A][B]) =
  match p
    | MkPair a _ -> a

snd(p Pair[A][B]) =
  match p
    | MkPair _ b -> b
```

Type parameters use brackets: `Pair[Int][Str]` at the call site, `Pair A B` in the declaration.

## Token comparison

Same `Shape` type and `area` function in three languages:

**ll-lang — 27 tokens**
```lll
Shape = Circle Float | Rect Float Float | Empty

area(s Shape) Float =
  | Circle r  -> 3.14159 * r * r
  | Rect w h  -> w * h
  | Empty     -> 0.0
```

**TypeScript — 89 tokens**
```typescript
type Shape =
  | { tag: "Circle"; r: number }
  | { tag: "Rect"; w: number; h: number }
  | { tag: "Empty" };

function area(s: Shape): number {
  switch (s.tag) {
    case "Circle": return Math.PI * s.r * s.r;
    case "Rect":   return s.w * s.h;
    case "Empty":  return 0;
  }
}
```

**Python — 72 tokens**
```python
from dataclasses import dataclass
from typing import Union

@dataclass
class Circle: r: float
@dataclass
class Rect: w: float; h: float
@dataclass
class Empty: pass

Shape = Union[Circle, Rect, Empty]

def area(s: Shape) -> float:
    match s:
        case Circle(r):    return 3.14159 * r * r
        case Rect(w, h):   return w * h
        case Empty():      return 0.0
```

ll-lang: **3.3× fewer tokens than TypeScript, 2.7× fewer than Python** for this pattern. Across the benchmark suite, type definitions average 3–5× fewer tokens vs TS/Python (see `benchmarks/results/token-benchmark.md`).

## Nested patterns

Patterns compose. Matching inside constructors:

```lll
Tree A = Leaf | Node (Tree A) A (Tree A)

depth(t Tree[A]) =
  | Leaf        -> 0
  | Node l _ r  -> 1 + max (depth l) (depth r)

-- Matching nested constructors in one arm
isLeftLeaf(t Tree[A]) =
  match t
    | Node Leaf _ _ -> true
    | _             -> false
```

## Next steps

- [03-building-a-parser.md](03-building-a-parser.md) — put this all together in a real parser
- [04-multi-target.md](04-multi-target.md) — compile the same code to TS, Python, Java
