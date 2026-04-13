module LLLang.Tests.CodegenCSharpTests

open Xunit
open LLLang.Elaborator
open LLLang.Compiler
open LLLang.Platform

let private csSrc (src: string) : string =
    match compileToCSharp src with
    | Ok cs -> cs
    | Error es -> failwith $"C# codegen failed: {es}"

[<Fact>]
let ``CSharp: compileToCSharp produces non-empty output`` () =
    let src = "module M\nlet x = 42"
    let cs = csSrc src
    Assert.False(System.String.IsNullOrWhiteSpace(cs))

[<Fact>]
let ``CSharp: output has C# backend header`` () =
    let src = "module M\nlet x = 1"
    let cs = csSrc src
    Assert.Contains("ll-lang C# backend", cs)

[<Fact>]
let ``CSharp: compileTarget CSharp produces same as compileToCSharp`` () =
    let src = "module M\nid(x Int) Int = x"
    let a = compileToCSharp src
    let b = compileTarget CSharp src
    Assert.Equal(a, b)

[<Fact>]
let ``CSharp: compileTarget CSharp does not emit F# let rec`` () =
    let src = "module M\nid(x Int) Int = x"
    match compileTarget CSharp src with
    | Ok cs ->
        Assert.DoesNotContain("\nlet rec", cs)
        Assert.Contains("public static class", cs)
    | Error es -> failwith $"unexpected error: {es}"

[<Fact>]
let ``CSharp: top-level numeric lets preserve literal values`` () =
    let src = "module M\nlet answer = 42\nlet lucky = 7"
    let cs = csSrc src
    Assert.Contains("public static readonly long answer = 42L;", cs)
    Assert.Contains("public static readonly long lucky = 7L;", cs)

[<Fact>]
let ``CSharp: arithmetic function body emits real expression`` () =
    let src = "module M\nadd(x Int)(y Int) Int = x + y"
    let cs = csSrc src
    Assert.Contains("Func<long, long> add(long x)", cs)
    Assert.Contains("y => (x + y)", cs)

[<Fact>]
let ``CSharp: prelude includes null-based Maybe helper runtime`` () =
    let src = "module M\nMaybe A = Some A | None\nf(m Maybe[Int]) Int = maybeWithDefault 0 m"
    let cs = csSrc src
    Assert.DoesNotContain("LLSome", cs)
    Assert.DoesNotContain("LLNone", cs)
    Assert.Contains("maybeWithDefault", cs)

[<Fact>]
let ``CSharp: prelude is omitted when stdlib helpers are not used`` () =
    let src = "module M\nid(x Int) Int = x"
    let cs = csSrc src
    Assert.DoesNotContain("// --- ll-lang stdlib (C#) ---", cs)

[<Fact>]
let ``CSharp: external console_log emits Console.WriteLine wrapper`` () =
    let src = "module M\nexternal console_log(msg Str) Unit"
    let cs = csSrc src
    Assert.Contains("public static void console_log(string msg) { Console.WriteLine(msg); }", cs)

[<Fact>]
let ``CSharp: external JSON_parse emits JsonSerializer.Deserialize wrapper`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let cs = csSrc src
    Assert.Contains("public static Any JSON_parse(string s) => System.Text.Json.JsonSerializer.Deserialize<object>(s);", cs)

[<Fact>]
let ``CSharp: JSON_parse declaration adds using System.Text.Json`` () =
    let src =
        "module M\n"
        + "opaque Any\n"
        + "external JSON_parse(s Str) Any\n"
        + "let _ = JSON_parse \"{\\\"a\\\": 1}\"\n"
    let cs = csSrc src
    Assert.Contains("using System.Text.Json;", cs)

[<Fact>]
let ``CSharp: modules without JSON_parse do not add System.Text.Json`` () =
    let src = "module M\nlet x = 1"
    let cs = csSrc src
    Assert.DoesNotContain("using System.Text.Json;", cs)

[<Fact>]
let ``CSharp: unknown external declaration raises E026`` () =
    let src = "module M\nexternal host_log(msg Str) Unit"
    match compileToCSharp src with
    | Ok cs -> failwith $"unexpected success: {cs}"
    | Error es ->
        let e = es |> List.exactlyOne
        Assert.Equal(E026, e.Code)
        Assert.Contains("UnknownExternalMapping", e.Message)
        Assert.Contains("target:csharp", e.Message)
        Assert.Contains("name:host_log", e.Message)

[<Fact>]
let ``CSharp: zero-arg constructor in expression emits object construction`` () =
    let src = "module M\nColor = Red | Green\nscore(c Color) Int = match c | Red -> 1 | Green -> 2\nmain() Int = score Red"
    let cs = csSrc src
    Assert.Contains("new Red()", cs)

[<Fact>]
let ``CSharp: match with constructor payload emits typed guarded accessor flow`` () =
    let src = "module M\nJson = JNull | JNum Int\nkind(v Json) Int = match v | JNull -> 0 | JNum n -> n\nmain() Int = kind (JNum 1)"
    let cs = csSrc src
    Assert.DoesNotContain("public static long kind(Json v) => 0L;", cs)
    Assert.Contains("__ll_match is JNum", cs)
    Assert.Contains("__ll_case_1._0", cs)
