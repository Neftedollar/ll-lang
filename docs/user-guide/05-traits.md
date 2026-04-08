# Traits

Traits are ll-lang's form of type classes. They work over type constructors,
not just concrete types — giving you higher-kinded abstractions like `Functor`
and `Monad`.

## Declaring a trait

```lll
trait Functor F =
  fn map(f A->B)(fa F[A]) F[B]
```

The trait name (`Functor`) is followed by one or more type variables (here
`F`). The body is an indented block of function signatures — no bodies.

`F` has kind `* -> *` because it's applied to `A` and `B` inside the signature.
`map` takes a function `A -> B`, an `F[A]`, and returns an `F[B]`.

Multiple methods:

```lll
trait Monad F =
  fn pure(a A) F[A]
  fn bind(fa F[A])(f A->F[B]) F[B]
```

## Implementing a trait

```lll
type Maybe A = Some A | None

impl Functor Maybe =
  fn map(f A->B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None
```

`impl TraitName TypeName = <indented block of fn decls>`. Each function must
match the signature declared in the trait, with `F` replaced by the impl
type (`Maybe`).

Multiple impls in the same module are allowed:

```lll
impl Functor Maybe = ...
impl Monad Maybe = ...
```

Internally the compiler mangles impl method names as `method_TypeName` (so
`map` in `impl Functor Maybe` becomes `map_Maybe` in the codegen output).

## Using a constrained generic

Declare a bracket constraint before the parameter list:

```lll
fn transform[F: Functor](xs F[A])(f A->B) F[B] = map f xs
```

Read as: "for any `F` that has a `Functor` impl". Inside the body you can
call `map` — the compiler resolves it to the correct `map_F` based on the
instantiated `F`.

## Worked example from the corpus

From `spec/examples/valid/04-traits.lll`:

```lll
module Examples.Traits

trait Functor F =
  fn map(f A->B)(fa F[A]) F[B]

trait Monad F =
  fn pure(a A) F[A]
  fn bind(fa F[A])(f A->F[B]) F[B]

type Maybe A = Some A | None

impl Functor Maybe =
  fn map(f A->B)(fa Maybe[A]) Maybe[B] =
    | Some a -> Some (f a)
    | None -> None

impl Monad Maybe =
  fn pure(a A) Maybe[A] = Some a
  fn bind(fa Maybe[A])(f A->Maybe[B]) Maybe[B] =
    | Some a -> f a
    | None -> None

fn transform[F: Functor](xs F[A])(f A->B) F[B] = map f xs
```

## Known limitations in the current phase

The trait dispatch machinery in `HMInfer.fs` generalizes over rigid type vars
correctly, and emits mangled impl method names in codegen. However:

- There is no automatic instance resolution at call sites yet. If you write
  `map (\x. x * 2) (Some 5)` the compiler does not automatically pick
  `map_Maybe` — you must call `map_Maybe` by its mangled name, or use a
  constrained generic like `transform`.
- `E006 MissingImpl` fires when a constrained generic is instantiated at a
  type that has no matching `impl`, but the constraint check is conservative.
- No superclass relations (`trait Monad F : Functor F`). A `Monad` impl is
  independent of its `Functor` impl in the current compiler.

The `04-traits.lll` corpus file type-checks and compiles but does not yet
exercise end-to-end dispatch at runtime.
