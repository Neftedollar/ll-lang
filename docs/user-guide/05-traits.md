# Traits

Traits are ll-lang's form of type classes. They work over type constructors,
not just concrete types — giving you higher-kinded abstractions like `Functor`
and `Monad`.

## Declaring a trait

```lll
trait Functor F =
  map(f A -> B)(fa F[A]) F[B]
```

The trait name (`Functor`) is followed by one or more type variables (here
`F`). The body is an indented block of method signatures — no bodies, no `fn`.

`F` has kind `* -> *` because it's applied to `A` and `B` inside the signature.
`map` takes a function `A -> B`, an `F[A]`, and returns an `F[B]`.

Multiple methods:

```lll
trait Monad F =
  pure(a A) F[A]
  bind(fa F[A])(f A -> F[B]) F[B]
```

## Implementing a trait

```lll
Maybe A = Some A | None

impl Functor Maybe =
  map(f A -> B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None
```

`impl TraitName TypeName = <indented block of method decls>`. Each method must
match the signature declared in the trait, with `F` replaced by the impl
type (`Maybe`).

Multiple impls in the same module are allowed:

```lll
impl Functor Maybe = ...
impl Monad Maybe = ...
```

Internally the compiler mangles impl method names as `TypeName_method` (so
`map` in `impl Functor Maybe` becomes `Maybe_map` in the codegen output).

## Using a constrained generic

Declare a bracket constraint before the parameter list:

```lll
transform[F: Functor](xs F[A])(f A -> B) = map f xs
```

Read as: "for any `F` that has a `Functor` impl". Inside the body you can
call `map` — the compiler resolves it to the correct `map_F` based on the
instantiated `F`.

## Worked example from the corpus

From `spec/examples/valid/04-traits.lll`:

```lll
module Examples.Traits

trait Functor F =
  map(f A -> B)(fa F[A]) F[B]

trait Monad F =
  pure(a A) F[A]
  bind(fa F[A])(f A -> F[B]) F[B]

Maybe A = Some A | None

impl Functor Maybe =
  map(f A -> B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None

impl Monad Maybe =
  pure(a A) Maybe[A] = Some a
  bind(fa Maybe[A])(f A -> Maybe[B]) Maybe[B] =
    | Some a -> f a
    | None -> None

transform[F: Functor](xs F[A])(f A -> B) = map f xs
```

## Current limits

Trait calls are resolved automatically in the current compiler (including
constrained generic call sites), and unresolved or ambiguous instances produce
`E006 MissingImpl`.

Remaining limitations:

- No superclass relations (`trait Monad F : Functor F`) in trait declarations.
- No default method bodies inside trait declarations.
