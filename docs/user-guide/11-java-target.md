# Java Target

ll-lang compiles to Java 21+. The output uses sealed interfaces, records, and switch expressions — modern Java features that map cleanly to ll-lang's algebraic types.

## Usage

```
lllc build --target java file.lll   # emits file.java
lllc build --target jvm  file.lll   # same (alias)
```

## Type Mapping

| ll-lang    | Java                          |
|------------|-------------------------------|
| `Int`      | `long`                        |
| `Float`    | `double`                      |
| `Str`      | `String`                      |
| `Bool`     | `boolean`                     |
| `Char`     | `char`                        |
| `Unit`     | `void`                        |
| `List[A]`  | `List<A>` (`java.util.List`)  |
| `Maybe[A]` | `Maybe<A>` (when declared in module) or `Optional<A>` (`java.util.Optional`) |

In generic positions (type parameters), primitive types are boxed automatically (`Long`, `Double`, `Boolean`, `Character`).

## Sum Types

Sum types become sealed interfaces with inner record classes:

```ll-lang
type Shape = Circle Float | Rect Float Float | Empty
```

Emits:

```java
sealed interface Shape permits Shape.Circle, Shape.Rect, Shape.Empty {
    record Circle(double _0) implements Shape {}
    record Rect(double _0, double _1) implements Shape {}
    record Empty() implements Shape {}
}
```

Parametric sum types use Java generics:

```ll-lang
type Maybe A = Some A | None
```

Emits:

```java
sealed interface Maybe<A> permits Maybe.Some, Maybe.None {
    record Some<A>(A _0) implements Maybe<A> {}
    record None<A>() implements Maybe<A> {}
}
```

## Functions

Single-parameter functions become static methods:

```ll-lang
fn double(x Int) Int = x * 2
```

Emits:

```java
public static long double_(long x) {
    return x * 2;
}
```

Curried (multi-parameter-group) functions return nested `Function`:

```ll-lang
fn add(a Int)(b Int) Int = a + b
```

Emits:

```java
public static Function<Long, Long> add(long a) {
    return b -> a + b;
}
```

The backend emits Java imports for commonly used JDK types (`List`, `Optional`, `Function`, `ArrayList`, `Collectors`, `Stream`, etc.), so generated signatures stay concise and idiomatic.

## Pattern Matching

Match expressions become ternary if/instanceof chains:

```ll-lang
fn area(s Shape) Float =
  | Circle r -> r * r * 3.14
  | Rect w h -> w * h
  | Empty -> 0.0
```

Emits:

```java
public static double area(Shape s) {
    return (s instanceof Shape.Circle _c0 ? _c0._0() * _c0._0() * 3.14
        : (s instanceof Shape.Rect _c1 ? _c1._0() * _c1._1()
        : 0.0));
}
```

## Module → Class

Each ll-lang module compiles to a Java class. The class name is derived from the last component of the module path:

```ll-lang
module Examples.Hello
fn main() = printfn "Hello!"
```

Emits a class `Hello` wrapping all declarations as `public static` members, with a `public static void main(String[] args)` entry point.

## Stdlib

The Java backend emits a `// --- ll-lang stdlib (Java) ---` helper block on demand (only when stdlib helpers are referenced). Helpers are lowered to private static methods, and call-sites are mapped to Java-safe helper names where needed (`abs -> abs_`, `min -> min_`, `max -> max_`, `print -> print_`, `exit -> exit_`).

Key functions:

| ll-lang | Java |
|---------|------|
| `printfn s` | `System.out.println(s)` |
| `strLen s` | `s.length()` |
| `strConcat a b` | `a + b` |
| `listMap f xs` | `xs.stream().map(f).collect(...)` |
| `listFold f acc xs` | loop |
| `intToStr n` | `Long.toString(n)` |

## Running the Output

```bash
lllc build --target java hello.lll
## lllc also prints SDK suggestions:
##   suggested compile: javac /.../Hello.java
##   suggested run: javac /.../Hello.java && java Hello
javac Hello.java
java Hello
```

Requires Java 21 or later.

When source filename and module-tail class name differ (for example `hello.lll` -> class `Hello`), `lllc` keeps the primary output (`hello.java`) and also writes a class-aligned mirror (`Hello.java`) for `javac`.

## Validation Status

Java output is covered by automated `javac` checks in the platform parity matrix for:

- arithmetic / conditionals
- ADT and tuple/string match
- trait impl dispatch (`impl_method`)
- constrained dispatch (`constrained_dispatch`)

This coverage runs in CI tests via `PlatformParityMatrixTests` when `javac` is available.

## Reserved Words

Java reserved words that conflict with ll-lang identifiers get a `_` suffix: `class_`, `void_`, `long_`, `interface_`, `record_`, `for_`, `while_`, etc.
