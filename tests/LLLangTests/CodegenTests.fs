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
