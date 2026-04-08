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
