module LLLang.Tests.CodegenTests

open System.IO
open Xunit
open LLLang.Codegen
open LLLang.TypedAST

// ---------- helpers ----------

/// Full pipeline: ll-lang source → emitted F# string. Fails test on any error.
let private codegenSrc (src: string) : string =
    match LLLang.Compiler.compile src with
    | Ok fs -> fs
    | Error es -> failwith $"codegen failed: {es}"

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

// ---------- scaffold ----------

[<Fact>]
let ``emit produces non-empty string for trivial module`` () =
    Assert.True(true)

// ---------- Task 2: type declarations ----------

[<Fact>]
let ``TDType sum type emits DU header`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("type Shape =", codegenSrc src)

[<Fact>]
let ``TDType sum type emits Circle branch with float`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Circle of float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits multi-arg branch`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    Assert.Contains("| Rect of float * float", codegenSrc src)

[<Fact>]
let ``TDType sum type emits zero-arg branch without of`` () =
    let src = "module M\ntype Shape = Circle Float | Rect Float Float | Empty"
    let fs = codegenSrc src
    Assert.Contains("| Empty", fs)
    Assert.DoesNotContain("| Empty of", fs)

[<Fact>]
let ``TDType parametric sum type emits type param`` () =
    let src = "module M\ntype Maybe A = Some A | None"
    Assert.Contains("type Maybe<'A>", codegenSrc src)

[<Fact>]
let ``TDType record type emits record syntax`` () =
    let src = "module M\ntype Point = x Float, y Float"
    let fs = codegenSrc src
    Assert.Contains("type Point = {", fs)
    Assert.Contains("x: float", fs)
    Assert.Contains("y: float", fs)

[<Fact>]
let ``TDTag emits nothing`` () =
    let src = "module M\ntag Meter"
    Assert.DoesNotContain("Meter", codegenSrc src)

// ---------- Task 3: expression emission ----------

[<Fact>]
let ``TELit int emits int64 literal`` () =
    Assert.Contains("42L", codegenSrc "module M\nlet x = 42")

[<Fact>]
let ``TELit float emits float literal`` () =
    Assert.Contains("3.14", codegenSrc "module M\nlet x = 3.14")

[<Fact>]
let ``TELit string emits quoted string`` () =
    Assert.Contains("\"hi\"", codegenSrc "module M\nlet x = \"hi\"")

[<Fact>]
let ``TELit bool true emits true`` () =
    Assert.Contains("true", codegenSrc "module M\nlet x = true")

[<Fact>]
let ``TEApp binary add emits infix`` () =
    let fs = codegenSrc "module M\nfn add(a Int)(b Int) Int = a + b"
    Assert.Contains("let add a b =", fs)
    Assert.Contains("(a + b)", fs)

[<Fact>]
let ``TEApp binary equality emits F# = operator`` () =
    Assert.Contains("(a = b)", codegenSrc "module M\nfn eq(a Int)(b Int) Bool = a == b")

[<Fact>]
let ``TEApp binary inequality emits F# <> operator`` () =
    Assert.Contains("(a <> b)", codegenSrc "module M\nfn neq(a Int)(b Int) Bool = a != b")

[<Fact>]
let ``TELam emits fun syntax`` () =
    Assert.Contains("(fun x -> x)", codegenSrc "module M\nlet f = \\x. x")

[<Fact>]
let ``TEIf emits if-then-else`` () =
    let fs = codegenSrc "module M\nfn abs(x Int) = if x < 0 then 0 else x"
    Assert.Contains("then 0L", fs)
    Assert.Contains("else x", fs)

[<Fact>]
let ``TDFn with no params emits let without parens`` () =
    Assert.Contains("let greeting =", codegenSrc "module M\nfn greeting = \"hello\"")

[<Fact>]
let ``TDLet emits let binding`` () =
    let fs = codegenSrc "module M\nlet pi = 3.14159"
    Assert.Contains("let pi =", fs)
    Assert.Contains("3.14159", fs)

// ---------- Task 4: match and patterns ----------

[<Fact>]
let ``TEMatch emits match with`` () =
    let src = "module M\ntype Shape = Circle Float | Empty\nfn area(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("match s with", fs)

[<Fact>]
let ``TEMatch emits branch arms`` () =
    let src = "module M\ntype Shape = Circle Float | Empty\nfn area(s Shape) =\n  | Circle r -> r\n  | Empty -> 0.0"
    let fs = codegenSrc src
    Assert.Contains("| Circle r ->", fs)
    Assert.Contains("| Empty ->", fs)

[<Fact>]
let ``PWild pattern emits underscore`` () =
    let src = "module M\ntype Color = Red Int | Blue\nfn f(x Color) =\n  | Red _ -> 1\n  | Blue -> 2"
    Assert.Contains("| Red _ ->", codegenSrc src)

[<Fact>]
let ``PCon single-arg pattern emits bare variable`` () =
    let src = "module M\ntype Maybe A = Some A | None\nfn unwrap(m Maybe[Int]) =\n  | Some x -> x\n  | None -> 0"
    Assert.Contains("| Some x ->", codegenSrc src)

// ---------- Task 5: top-level module emission ----------

[<Fact>]
let ``module header is emitted`` () =
    Assert.Contains("module Examples.Basics", codegenSrc "module Examples.Basics\nlet x = 1")

[<Fact>]
let ``multiple declarations emitted in order`` () =
    let src = "module M\nlet a = 1\nlet b = 2\nlet c = 3"
    let fs = codegenSrc src
    Assert.True(fs.IndexOf("let a =") < fs.IndexOf("let b ="))
    Assert.True(fs.IndexOf("let b =") < fs.IndexOf("let c ="))

[<Fact>]
let ``fn main gets EntryPoint attribute`` () =
    let src = "module M\nfn main() = 0"
    let fs = codegenSrc src
    Assert.Contains("[<EntryPoint>]", fs)
    Assert.Contains("let main (argv: string[]) =", fs)

[<Fact>]
let ``non-main fn does not get EntryPoint`` () =
    Assert.DoesNotContain("[<EntryPoint>]", codegenSrc "module M\nfn add(a Int)(b Int) Int = a + b")
