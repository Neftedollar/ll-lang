module LLLang.Tests.ReverseTranspilerTests

open System
open System.IO
open System.Diagnostics
open Xunit
open LLLang.Compiler
open LLLang.Platform
open LLLang.ReverseTranspiler
open LLLang.FParsecParser

let private sampleSrc =
    "module Demo\nlet answer = 42\nlet lucky = 7\n"

let private sampleSrcWithBoolsAndStrings =
    "module Demo\nlet answer = 42\nlet ok = true\nlet name = \"Neo\"\n"

let private sampleSrcWithFloats =
    "module Demo\nlet pi = 3.5\nlet ratio = -0.125\n"

let private sampleSrcWithChars =
    "module Demo\nlet initial = 'N'\n"

let private sampleFunctionSrc =
    "module Demo\ninc(x Int) = x + 1\nmain() = inc 41\n"

[<Fact>]
let ``reverse parser recovers numeric lets from all primary platform targets`` () =
    for target in [FSharp; TypeScript; Python; CSharp; LLVM] do
        let emitted =
            match compileTarget target sampleSrc with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Contains("let answer = 42", reversed)
        Assert.Contains("let lucky = 7", reversed)

        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target}: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers bool and string lets from non-LLVM platform targets`` () =
    for target in [FSharp; TypeScript; Python; CSharp] do
        let emitted =
            match compileTarget target sampleSrcWithBoolsAndStrings with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Contains("let ok = true", reversed)
        Assert.Contains("let name = \"Neo\"", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target}: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers bool lets from LLVM i1 globals`` () =
    let emitted =
        match compileTarget LLVM sampleSrcWithBoolsAndStrings with
        | Ok code -> code
        | Error es -> Assert.Fail($"compileTarget LLVM failed: {es}"); ""
    let reversed =
        match reverseToLll LLVM emitted with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll LLVM failed: {msg}"); ""
    Assert.Contains("let ok = true", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for LLVM: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers float lets from all primary platform targets`` () =
    for target in [FSharp; TypeScript; Python; Java; CSharp; LLVM] do
        let emitted =
            match compileTarget target sampleSrcWithFloats with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Contains("let pi = 3.5", reversed)
        Assert.Contains("let ratio = -0.125", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target}: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers char lets from typed platform targets`` () =
    for target in [FSharp; Java; CSharp; LLVM] do
        let emitted =
            match compileTarget target sampleSrcWithChars with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Contains("let initial = 'N'", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target}: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers simple function declarations from all primary platform targets`` () =
    for target in [FSharp; TypeScript; Python; Java; CSharp; LLVM] do
        let emitted =
            match compileTarget target sampleFunctionSrc with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Contains("inc(", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target}: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers both lets and functions from one module`` () =
    let src = "module Demo\nlet answer = 42\ninc(x Int) = x + 1\n"
    let emitted =
        match compileTarget TypeScript src with
        | Ok code -> code
        | Error es -> Assert.Fail($"compileTarget TypeScript failed: {es}"); ""
    let reversed =
        match reverseToLll TypeScript emitted with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""

    Assert.Contains("let answer = 42", reversed)
    Assert.Contains("inc(", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for TypeScript mixed module: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles idiomatic Java/CSharp block-bodied methods`` () =
    let javaSrc = """
public class Demo {
    public static long inc(long x) {
        return x + 1L;
    }
}
"""
    let csSrc = """
public static class Demo {
    public static long inc(long x) {
        return x + 1L;
    }
}
"""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""

    Assert.Contains("inc(", javaReversed)
    Assert.Contains("inc(", csReversed)
    match parseModuleWithPos javaReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Java block method: {e}\n{javaReversed}")
    match parseModuleWithPos csReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for C# block method: {e}\n{csReversed}")

[<Fact>]
let ``reverse parser handles broad hand-written TypeScript function shapes`` () =
    let tsSrc = """
const Answer = 42;
const inc = (x: number) => { return x + 1; };
const dec = x => x - 1;
function id<T>(x: T): T { return x; }
"""
    let reversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""

    Assert.Contains("let answer = 42", reversed)
    Assert.Contains("inc(", reversed)
    Assert.Contains("dec(", reversed)
    Assert.Contains("id(", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for TypeScript broad shapes: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles semicolonless TypeScript function shapes`` () =
    let tsSrc = """
const Answer: number = 42
const inc = (x: number) => x + 1
function clamp(x: number): number {
    if (x > 0) {
        return x
    } else {
        return 0
    }
}
"""
    let reversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""

    Assert.Contains("let answer = 42", reversed)
    Assert.Contains("inc(", reversed)
    Assert.Contains("if x > 0", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for semicolonless TypeScript shapes: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles broad hand-written Python shapes`` () =
    let pySrc = """
Answer = 42
def Inc(x: int) -> int: return x + 1
def dec(x: int) -> int:
    return x - 1
"""
    let reversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""

    Assert.Contains("let answer = 42", reversed)
    Assert.Contains("inc(", reversed)
    Assert.Contains("dec(", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Python broad shapes: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers typed top-level constants in TypeScript and Python`` () =
    let tsSrc = """
const Answer: number = 42;
const Ok: boolean = true;
const Name: string = "Neo";
"""
    let pySrc = """
Answer: int = 42
Ok: bool = True
Name: str = "Neo"
"""
    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let pyReversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""

    Assert.Contains("let answer = 42", tsReversed)
    Assert.Contains("let ok = true", tsReversed)
    Assert.Contains("let name = \"Neo\"", tsReversed)
    Assert.Contains("let answer = 42", pyReversed)
    Assert.Contains("let ok = true", pyReversed)
    Assert.Contains("let name = \"Neo\"", pyReversed)

    match parseModuleWithPos tsReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for typed TypeScript constants: {e}\n{tsReversed}")
    match parseModuleWithPos pyReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for typed Python constants: {e}\n{pyReversed}")

[<Fact>]
let ``reverse parser handles broad hand-written CSharp and Java shapes`` () =
    let csSrc = """
public static class Demo {
    private const long Answer = 42;
    private static long Inc(long x) => x + 1L;
}
"""
    let javaSrc = """
public class Demo {
    private static final boolean Ok = true;
    protected static long Inc(long x) { return x + 1L; }
}
"""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    Assert.Contains("let answer = 42", csReversed)
    Assert.Contains("inc(", csReversed)
    Assert.Contains("let ok = true", javaReversed)
    Assert.Contains("inc(", javaReversed)
    match parseModuleWithPos csReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for C# broad shapes: {e}\n{csReversed}")
    match parseModuleWithPos javaReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Java broad shapes: {e}\n{javaReversed}")

[<Fact>]
let ``reverse parser handles idiomatic CSharp and Java int-float field declarations`` () =
    let csSrc = """
public static class Demo {
    private const int Count = 2;
    private static readonly float Ratio = 0.5f;
}
"""
    let javaSrc = """
public class Demo {
    private static final int Count = 2;
    private static final float Ratio = 0.5f;
}
"""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    Assert.Contains("let count = 2", csReversed)
    Assert.Contains("let ratio = 0.5", csReversed)
    Assert.Contains("let count = 2", javaReversed)
    Assert.Contains("let ratio = 0.5", javaReversed)
    match parseModuleWithPos csReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for C# int/float fields: {e}\n{csReversed}")
    match parseModuleWithPos javaReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Java int/float fields: {e}\n{javaReversed}")

[<Fact>]
let ``reverse parser lowers top-level ternary expressions to ll if-then-else`` () =
    let tsSrc = """
function clamp(x: number): number { return x > 0 ? x : 0; }
"""
    let csSrc = """
public static class Demo {
    public static long Clamp(long x) => x > 0 ? x : 0L;
}
"""
    let javaSrc = """
public class Demo {
    public static long Clamp(long x) { return x > 0 ? x : 0L; }
}
"""

    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    Assert.Contains("if x > 0", tsReversed)
    Assert.Contains("\n  x\nelse 0", tsReversed)
    Assert.Contains("if x > 0", csReversed)
    Assert.Contains("\n  x\nelse 0", csReversed)
    Assert.Contains("if x > 0", javaReversed)
    Assert.Contains("\n  x\nelse 0", javaReversed)

    match parseModuleWithPos tsReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for TypeScript ternary lowering: {e}\n{tsReversed}")
    match parseModuleWithPos csReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for C# ternary lowering: {e}\n{csReversed}")
    match parseModuleWithPos javaReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Java ternary lowering: {e}\n{javaReversed}")

[<Fact>]
let ``reverse parser lowers Python ternary expressions to ll if-else`` () =
    let pySrc = """
def clamp(x: int) -> int:
    return x if x > 0 else 0
"""
    let pyReversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""

    Assert.Contains("if x > 0", pyReversed)
    Assert.Contains("\n  x\nelse 0", pyReversed)
    match parseModuleWithPos pyReversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for Python ternary lowering: {e}\n{pyReversed}")

[<Fact>]
let ``reverse parser normalizes TypeScript strict equality operators`` () =
    let tsSrc = """
function clamp(x: number): number {
    if (x !== 0) {
        return x === 1 ? 10 : 20;
    }
    return 0;
}
"""
    let reversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""

    Assert.DoesNotContain("===", reversed)
    Assert.DoesNotContain("!==", reversed)
    Assert.Contains("if x != 0", reversed)
    Assert.Contains("\n  if x == 1", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for TS strict equality normalization: {e}\n{reversed}")

[<Fact>]
let ``reverse parser normalizes FSharp equality operators in if conditions`` () =
    let src = """
module Demo
isDigit(c Char) = if charIsDigit c
  if c == '0'
    false
  else true
else false
"""
    let emitted =
        match compileTarget FSharp src with
        | Ok code -> code
        | Error es -> Assert.Fail($"compileTarget FSharp failed: {es}"); ""
    let reversed =
        match reverseToLll FSharp emitted with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("c == '0'", reversed)
    Assert.DoesNotContain("c = '0'", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for F# equality normalization: {e}\n{reversed}")

[<Fact>]
let ``reverse parser flattens FSharp let-in chains and tuple-style constructor calls`` () =
    let fsSrc = """
module Demo
let mk x = (let p = (Pair(1L, x)) in (if x = 0L then p else Pair(x, 1L)))
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("p = Pair 1 x", reversed)
    Assert.Contains("if x == 0", reversed)
    Assert.DoesNotContain("Pair(1, x)", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for F# let-in/tuple ctor lowering: {e}\n{reversed}")

[<Fact>]
let ``reverse parser keeps match branch shape for FSharp let-in if expressions`` () =
    let fsSrc = """
module X

let runOkCase name src wantKind =
    (match (parse src) with
     | ParseErr(e) -> ((mkFail name) ((strConcat "parse-error:") e))
     | ParseOk(v, _) ->
         (let got = (kindOf v) in
          (if (got = wantKind) then (mkOk name) else ((mkFail name) ((strConcat "kind-mismatch:") got)))))
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("match (parse src)", reversed)
    Assert.Contains("| ParseOk", reversed)
    Assert.Contains("if (kindOf v) == wantKind", reversed)
    Assert.DoesNotContain("if (kindOf v) == wantKind\nmkOk name\n  match (parse src)", reversed)

[<Fact>]
let ``reverse parser recovers FSharp sum type declarations used by JSON-style modules`` () =
    let fsSrc = """
module Std.Json

type Maybe<'A> = 'A option

type JsonField =
    | JField of string * JsonValue
and JsonValue =
    | JNull
    | JStr of string
    | JObj of JsonField list

let kindOf(v: JsonValue) =
    match v with
    | JNull -> "null"
    | JStr(_) -> "string"
    | JObj(_) -> "object"
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("Maybe A = Some A | None", reversed)
    Assert.Contains("JsonField =", reversed)
    Assert.Contains("| JField Str JsonValue", reversed)
    Assert.Contains("JsonValue =", reversed)
    Assert.Contains("| JObj List[JsonField]", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for F# type decl recovery: {e}\n{reversed}")

[<Fact>]
let ``reverse parser normalizes inline FSharp match-with arms into ll offside layout`` () =
    let fsSrc = """
module Demo
let classify x = match x with | 0L -> "zero" | _ -> "non-zero"
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.DoesNotContain(" with ", reversed)
    Assert.Contains("match x", reversed)
    Assert.Contains("| 0 -> \"zero\"", reversed)
    Assert.Contains("| _ -> \"non-zero\"", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for inline match normalization: {e}\n{reversed}")

[<Fact>]
let ``reverse parser keeps constructor casing while normalizing bool literals`` () =
    let fsSrc = """
module Demo
type Token =
    | TTrue
    | TFalse
let isTruthy tok = match tok with | TTrue -> True | TFalse -> False
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("TTrue", reversed)
    Assert.Contains("TFalse", reversed)
    Assert.DoesNotContain("Ttrue", reversed)
    Assert.DoesNotContain("Tfalse", reversed)
    Assert.Contains("-> true", reversed)
    Assert.Contains("-> false", reversed)

    match compileTarget FSharp reversed with
    | Ok _ -> ()
    | Error es -> Assert.Fail($"reversed source failed full compile: {es}\n{reversed}")

[<Fact>]
let ``reverse parser handles block if-else return functions in TS Python CSharp Java`` () =
    let tsSrc = """
function clamp(x: number): number {
    if (x > 0) {
        return x;
    } else {
        return 0;
    }
}
"""
    let pySrc = """
def clamp(x: int) -> int:
    if x > 0:
        return x
    else:
        return 0
"""
    let csSrc = """
public static class Demo {
    public static long Clamp(long x) {
        if (x > 0) { return x; } else { return 0L; }
    }
}
"""
    let javaSrc = """
public class Demo {
    public static long Clamp(long x) {
        if (x > 0) { return x; } else { return 0L; }
    }
}
"""

    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let pyReversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    for reversed in [tsReversed; pyReversed; csReversed; javaReversed] do
        Assert.Contains("if x > 0", reversed)
        Assert.Contains("\n  x\nelse 0", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for block if/else function: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles block if-return fallback-return functions in TS CSharp Java`` () =
    let tsSrc = """
function clamp(x: number): number {
    if (x > 0) {
        return x;
    }
    return 0;
}
"""
    let csSrc = """
public static class Demo {
    public static long Clamp(long x) {
        if (x > 0) { return x; }
        return 0L;
    }
}
"""
    let javaSrc = """
public class Demo {
    public static long Clamp(long x) {
        if (x > 0) { return x; }
        return 0L;
    }
}
"""

    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    for reversed in [tsReversed; csReversed; javaReversed] do
        Assert.Contains("if x > 0", reversed)
        Assert.Contains("\n  x\nelse 0", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for block if+fallback function: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles block if-elseif-else return functions in TS Python CSharp Java`` () =
    let tsSrc = """
function classify(x: number): number {
    if (x > 0) {
        return 1;
    } else if (x == 0) {
        return 0;
    } else {
        return -1;
    }
}
"""
    let pySrc = """
def classify(x: int) -> int:
    if x > 0:
        return 1
    elif x == 0:
        return 0
    else:
        return -1
"""
    let csSrc = """
public static class Demo {
    public static long Classify(long x) {
        if (x > 0) { return 1L; } else if (x == 0) { return 0L; } else { return -1L; }
    }
}
"""
    let javaSrc = """
public class Demo {
    public static long Classify(long x) {
        if (x > 0) { return 1L; } else if (x == 0) { return 0L; } else { return -1L; }
    }
}
"""

    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let pyReversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    for reversed in [tsReversed; pyReversed; csReversed; javaReversed] do
        Assert.Contains("if x > 0", reversed)
        Assert.Contains("else if x == 0", reversed)
        Assert.Contains("\n  0\nelse -1", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for block if/elseif/else function: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles block if-elseif with fallback return functions in TS Python CSharp Java`` () =
    let tsSrc = """
function classify(x: number): number {
    if (x > 0) {
        return 1;
    } else if (x == 0) {
        return 0;
    }
    return -1;
}
"""
    let pySrc = """
def classify(x: int) -> int:
    if x > 0:
        return 1
    elif x == 0:
        return 0
    return -1
"""
    let csSrc = """
public static class Demo {
    public static long Classify(long x) {
        if (x > 0) { return 1L; } else if (x == 0) { return 0L; }
        return -1L;
    }
}
"""
    let javaSrc = """
public class Demo {
    public static long Classify(long x) {
        if (x > 0) { return 1L; } else if (x == 0) { return 0L; }
        return -1L;
    }
}
"""

    let tsReversed =
        match reverseToLll TypeScript tsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll TypeScript failed: {msg}"); ""
    let pyReversed =
        match reverseToLll Python pySrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Python failed: {msg}"); ""
    let csReversed =
        match reverseToLll CSharp csSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll CSharp failed: {msg}"); ""
    let javaReversed =
        match reverseToLll Java javaSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll Java failed: {msg}"); ""

    for reversed in [tsReversed; pyReversed; csReversed; javaReversed] do
        Assert.Contains("if x > 0", reversed)
        Assert.Contains("else if x == 0", reversed)
        Assert.Contains("\n  0\nelse -1", reversed)
        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for block if/elseif+fallback function: {e}\n{reversed}")

[<Fact>]
let ``reverse parser recovers curried functions from idiomatic TS Python CSharp Java output`` () =
    let src = "module Demo\nadd(x Int)(y Int) = x + y\nmain() = add 1 2\n"

    for target in [TypeScript; Python; CSharp; Java] do
        let emitted =
            match compileTarget target src with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget {target} failed: {es}"); ""

        let reversed =
            match reverseToLll target emitted with
            | Ok lll -> lll
            | Error msg -> Assert.Fail($"reverseToLll {target} failed: {msg}"); ""

        Assert.Matches(@"add\(x\)\(y\)\s*=\s*\(?x \+ y\)?", reversed)

        match parseModuleWithPos reversed with
        | Ok _ -> ()
        | Error e -> Assert.Fail($"reverse output parse failed for {target} curried recovery: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles hand-written typed FSharp functions`` () =
    let fsSrc = """
module Demo
let Inc (x: int64) : int64 = x + 1L
let rec dec (x: int64) : int64 =
    x - 1L
"""
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed: {msg}"); ""

    Assert.Contains("inc(", reversed)
    Assert.Contains("dec(", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for typed F# shapes: {e}\n{reversed}")

[<Fact>]
let ``reverse parser handles broader LLVM arithmetic and call subset`` () =
    let llSrc = """
define i64 @inc(i64 %x) {
entry:
  %tmp = add i64 %x, 1
  ret i64 %tmp
}

define i64 @dec(i64 %x) {
entry:
  %tmp = sub i64 %x, 1
  ret i64 %tmp
}

define i64 @mul2(i64 %x) {
entry:
  %tmp = mul i64 %x, 2
  ret i64 %tmp
}

define i64 @div2(i64 %x) {
entry:
  %tmp = sdiv i64 %x, 2
  ret i64 %tmp
}

define i64 @id(i64 %x) {
entry:
  ret i64 %x
}

define i64 @main() {
entry:
  %tmp = call i64 @inc(i64 41)
  ret i64 %tmp
}
"""
    let reversed =
        match reverseToLll LLVM llSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll LLVM failed: {msg}"); ""

    Assert.Contains("inc(", reversed)
    Assert.Contains("dec(", reversed)
    Assert.Contains("mul2(", reversed)
    Assert.Contains("div2(", reversed)
    Assert.Contains("id(", reversed)
    Assert.Contains("main()", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for broader LLVM subset: {e}\n{reversed}")

[<Fact>]
let ``reverse parser reports error when no declarations are recoverable`` () =
    match reverseToLll TypeScript "function main(): void { return; }\n" with
    | Ok lll -> Assert.Fail($"expected error, got:\n{lll}")
    | Error msg ->
        Assert.Contains("could not recover", msg)

let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))

let private lllcDll =
    Path.Combine(repoRoot, "src/LLLangTool/bin/Debug/net10.0/lllc.dll")

let private runLllc (cwd: string) (args: string list) =
    let psi = ProcessStartInfo("dotnet")
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.ArgumentList.Add(lllcDll)
    for arg in args do
        psi.ArgumentList.Add(arg)
    use proc = LLLang.Tests.TestCompat.startProcess psi
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    (proc.ExitCode, stdout, stderr)

[<Fact>]
let ``reverse parser boomerang keeps stdlib Json FSharp source compilable`` () =
    let fsPath = Path.Combine(repoRoot, "stdlib/src/Json.fs")
    Assert.True(File.Exists(fsPath), $"missing stdlib Json source: {fsPath}")

    let fsSrc = File.ReadAllText(fsPath)
    let reversed =
        match reverseToLll FSharp fsSrc with
        | Ok lll -> lll
        | Error msg -> Assert.Fail($"reverseToLll FSharp failed for stdlib Json: {msg}"); ""

    Assert.Contains("module Std.Json", reversed)
    match parseModuleWithPos reversed with
    | Ok _ -> ()
    | Error e -> Assert.Fail($"reverse output parse failed for stdlib Json: {e}\n{reversed}")

    match compileTarget FSharp reversed with
    | Ok _ -> ()
    | Error es -> Assert.Fail($"reversed stdlib Json failed full compile: {es}\n{reversed}")

[<Fact>]
let ``lllc reverse command supports Platform.*.SDK aliases`` () =
    let tempDir = Path.Combine(Path.GetTempPath(), "lll-reverse-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tempDir) |> ignore
    try
        let tsIn = Path.Combine(tempDir, "input.ts")
        let tsCode =
            match compileTarget TypeScript sampleSrcWithBoolsAndStrings with
            | Ok code -> code
            | Error es -> Assert.Fail($"compileTarget TypeScript failed: {es}"); ""
        File.WriteAllText(tsIn, tsCode)

        let (exitCode, stdout, stderr) =
            runLllc tempDir ["reverse"; "--from"; "Platform.TypeScript.SDK"; tsIn]

        Assert.True((exitCode = 0), $"reverse command failed: exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}")

        let outPath = Path.Combine(tempDir, "input.reversed.lll")
        Assert.True(File.Exists(outPath), $"missing reverse output file: {outPath}")
        let outText = File.ReadAllText(outPath)
        Assert.Contains("module ", outText)
        Assert.Contains("let answer = 42", outText)
        Assert.Contains("let ok = true", outText)
        Assert.Contains("let name = \"Neo\"", outText)
    finally
        try Directory.Delete(tempDir, true) with _ -> ()
