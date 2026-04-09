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
| `List[A]`  | `java.util.List<A>`           |
| `Maybe[A]` | `java.util.Optional<A>`       |

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
public static java.util.function.Function<Long, Long> add(long a) {
    return b -> a + b;
}
```

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

The Java backend embeds a `// --- ll-lang stdlib (Java) ---` block with private static implementations of all stdlib functions. Key functions:

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
javac hello.java
java Hello
```

Requires Java 21 or later.

## Reserved Words

Java reserved words that conflict with ll-lang identifiers get a `_` suffix: `class_`, `void_`, `long_`, `interface_`, `record_`, `for_`, `while_`, etc.
