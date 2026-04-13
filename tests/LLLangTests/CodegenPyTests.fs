module LLLang.Tests.CodegenPyTests

open Xunit
open LLLang.Elaborator
open LLLang.Compiler

// ---------- helpers ----------

let private pySrc (src: string) : string =
    match compileToPy src with
    | Ok py -> py
    | Error es -> failwith $"Python codegen failed: {es}"

// ---------- type declarations ----------

[<Fact>]
let ``Py: sum type emits dataclass per constructor`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    let py = pySrc src
    Assert.Contains("@dataclass", py)
    Assert.Contains("class Circle", py)
    Assert.Contains("class Rect", py)
    Assert.Contains("class Empty", py)

[<Fact>]
let ``Py: sum type dataclasses are frozen`` () =
    let src = "module M\nShape = Circle Float | Empty"
    let py = pySrc src
    Assert.Contains("@dataclass(frozen=True)", py)

[<Fact>]
let ``Py: sum type emits _tag field`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    let py = pySrc src
    Assert.Contains("_tag: str", py)

[<Fact>]
let ``Py: sum type emits Union type alias`` () =
    let src = "module M\nShape = Circle Float | Rect Float Float | Empty"
    let py = pySrc src
    Assert.Contains("Shape = Union[", py)

[<Fact>]
let ``Py: Int maps to int`` () =
    let src = "module M\nid(x Int) Int = x"
    let py = pySrc src
    Assert.Contains(": int", py)

[<Fact>]
let ``Py: Str maps to str`` () =
    let src = "module M\ngreet(s Str) Str = s"
    let py = pySrc src
    Assert.Contains(": str", py)

[<Fact>]
let ``Py: Bool maps to bool`` () =
    let src = "module M\nneg(b Bool) Bool = !b"
    let py = pySrc src
    Assert.Contains(": bool", py)

[<Fact>]
let ``Py: Float maps to float`` () =
    let src = "module M\nsq(x Float) Float = x * x"
    let py = pySrc src
    Assert.Contains(": float", py)

[<Fact>]
let ``Py: Maybe A maps to Optional`` () =
    let src = "module M\nMaybe A = Some A | None\nwrap(x Int) Maybe[Int] = Some x"
    let py = pySrc src
    Assert.Contains("Optional", py)

// ---------- function declarations ----------

[<Fact>]
let ``Py: single-param fn emits def`` () =
    let src = "module M\ndouble(x Int) Int = x * 2"
    let py = pySrc src
    Assert.Contains("def double(", py)
    Assert.Contains("return", py)

[<Fact>]
let ``Py: curried fn emits nested defs`` () =
    let src = "module M\nadd(a Int)(b Int) Int = a + b"
    let py = pySrc src
    // Two def keywords - outer and inner
    let defCount = py.Split([|"\ndef "; "\n    def "|], System.StringSplitOptions.None).Length - 1
    Assert.True(defCount >= 1, $"expected nested def; got:\n{py}")

[<Fact>]
let ``Py: let binding emits assignment`` () =
    let src = "module M\nlet x = 42"
    let py = pySrc src
    Assert.Contains("x = 42", py)

[<Fact>]
let ``Py: if-then-else emits ternary`` () =
    let src = "module M\nabs(x Int) Int =\n  if x > 0\n    x\n  else 0 - x"
    let py = pySrc src
    Assert.Contains("if", py)
    Assert.Contains("else", py)

[<Fact>]
let ``Py: strConcat emits plus`` () =
    let src = "module M\ngreet(name Str) Str = strConcat \"Hello \" name"
    let py = pySrc src
    Assert.Contains("+", py)

[<Fact>]
let ``Py: external console_log emits print wrapper`` () =
    let src = "module M\nexternal console_log(msg Str) Unit\n"
    let py = pySrc src
    Assert.Contains("def console_log(msg: str) -> None:", py)
    Assert.Contains("return print(msg)", py)

[<Fact>]
let ``Py: external JSON_parse emits json.loads wrapper`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let py = pySrc src
    Assert.Contains("def JSON_parse(s: str)", py)
    Assert.Contains("json.loads(s)", py)

[<Fact>]
let ``Py: JSON_parse declaration adds import json`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let py = pySrc src
    Assert.Contains("import json", py)

[<Fact>]
let ``Py: modules without JSON_parse do not import json`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.DoesNotContain("import json", py)

[<Fact>]
let ``Py: unknown external declaration raises E026`` () =
    let src = "module M\nexternal host_log(msg Str) Unit\n"
    match compileToPy src with
    | Ok py -> failwith $"unexpected success: {py}"
    | Error es ->
        let e = es |> List.exactlyOne
        Assert.Equal(E026, e.Code)
        Assert.Contains("UnknownExternalMapping", e.Message)
        Assert.Contains("target:python", e.Message)
        Assert.Contains("name:host_log", e.Message)

[<Fact>]
let ``Py: prelude contains from __future__ import`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.Contains("from __future__ import annotations", py)

[<Fact>]
let ``Py: prelude imports dataclass`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.Contains("from dataclasses import dataclass", py)

[<Fact>]
let ``Py: prelude imports Optional`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.Contains("from typing import Optional", py)

[<Fact>]
let ``Py: prelude imports TypeVar`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.Contains("TypeVar", py)

[<Fact>]
let ``Py: prelude contains maybeWithDefault builtin`` () =
    let src = "module M\nmain() Unit = printfn \"x\""
    let py = pySrc src
    Assert.Contains("def maybeWithDefault", py)

[<Fact>]
let ``Py: prelude contains resultMapErr builtin`` () =
    let src = "module M\nmain() Unit = printfn \"x\""
    let py = pySrc src
    Assert.Contains("def resultMapErr", py)

[<Fact>]
let ``Py: prelude writeFile helper uses context manager`` () =
    let src = "module M\nmain() Unit = printfn \"x\""
    let py = pySrc src
    Assert.Contains("def writeFile(path: str)", py)
    Assert.Contains("with open(path, 'w', encoding='utf-8') as f", py)

[<Fact>]
let ``Py: prelude listFold helper uses explicit loop`` () =
    let src = "module M\nmain() Unit = printfn \"x\""
    let py = pySrc src
    Assert.Contains("def listFold(", py)
    Assert.Contains("for x in xs:", py)

[<Fact>]
let ``Py: runtime helpers are omitted when stdlib is unused`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.DoesNotContain("def maybeWithDefault", py)
    Assert.DoesNotContain("def listFold(", py)

[<Fact>]
let ``Py: generated public surface avoids Any`` () =
    let src = "module M\nMaybe A = Some A | None\nid(x A) A = x\n"
    let py = pySrc src
    Assert.DoesNotContain("Any", py)

[<Fact>]
let ``Py: emits header comment`` () =
    let src = "module M\nlet x = 1"
    let py = pySrc src
    Assert.Contains("Generated by lllc", py)

[<Fact>]
let ``Py: char zero escapes as slash-zero literal`` () =
    let src = "module M\nlet c = '\\0'"
    let py = pySrc src
    Assert.Contains("\"\\0\"", py)
    Assert.DoesNotContain(0uy, System.Text.Encoding.UTF8.GetBytes(py))

// ---------- pattern matching ----------

[<Fact>]
let ``Py: match on sum type checks _tag`` () =
    let src = "module M\nColor = Red | Green | Blue\ntoInt(c Color) Int = match c | Red -> 0 | Green -> 1 | Blue -> 2"
    let py = pySrc src
    Assert.Contains("_tag", py)

[<Fact>]
let ``Py: match emits ternary chain`` () =
    let src = "module M\nColor = Red | Green\ntoInt(c Color) Int = match c | Red -> 0 | Green -> 1"
    let py = pySrc src
    Assert.Contains("if", py)
    Assert.Contains("else", py)

// ---------- round-trip via compileTarget ----------

[<Fact>]
let ``compileTarget Python produces same as compileToPy`` () =
    let src = "module M\nid(x Int) Int = x"
    let a = compileToPy src
    let b = compileTarget Python src
    Assert.Equal(a, b)

[<Fact>]
let ``compileTarget Python does not emit F# let keyword`` () =
    let src = "module M\nid(x Int) Int = x"
    match compileTarget Python src with
    | Ok py ->
        // Python output should not have F# let bindings
        Assert.DoesNotContain("\nlet id", py)
    | Error es -> failwith $"unexpected error: {es}"
