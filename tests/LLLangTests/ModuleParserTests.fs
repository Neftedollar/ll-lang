module LLLang.Tests.ModuleParserTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.HMInfer

// Phase 7.4: tests for the full-module parser written in ll-lang itself.
// Lives in spec/examples/valid/15-moduleparser-real.lll. This is the
// showcase milestone: lexer + type-decl + fn-decl + expression parsers
// stitched into one program that consumes a whole `module M\n type ...
// \n fn ... = ...` source string and produces a `List[Decl]` AST.
// Two layers of coverage:
//   1. inference round-trip — parses, elaborates, infers without errors.
//   2. runtime — `lllc run` produces the expected pretty form for each
//      decl in the hardcoded driver input (module header, two type
//      decls, three fn decls covering int literal, binary body, and
//      `if-then-else` body).

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

[<Fact>]
let ``15-moduleparser-real.lll parses, elaborates, and infers without errors`` () =
    let src = readValid "15-moduleparser-real.lll"
    match tokenize src |> Result.bind parseModuleWithPos with
    | Error e -> Assert.Fail($"parse: {e}")
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Assert.Fail($"elaborator: {es}")
        | Ok (m', env) ->
            match infer pm m' env with
            | Error es -> Assert.Fail($"infer: {es}")
            | Ok tm -> Assert.NotNull(tm.Env)

[<Fact>]
let ``15-moduleparser-real.lll runs and pretty-prints a full module AST`` () =
    let lllPath =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../spec/examples/valid/15-moduleparser-real.lll")
    let llcDll =
        Path.Combine(
            __SOURCE_DIRECTORY__,
            "../../src/LLLangTool/bin/Debug/net10.0/lllc.dll")
    let psi = System.Diagnostics.ProcessStartInfo("dotnet", $"\"{llcDll}\" run \"{lllPath}\"")
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError  <- true
    psi.UseShellExecute        <- false
    use proc = System.Diagnostics.Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    // Phase 7.5e extends the Phase 7.5d driver with three more module-
    // level decl forms from the Phase 7.5 backlog: **`tag Name`** decls
    // (`DTag`), **`import Foo.Bar`** decls (`DImport`, dot-joined path),
    // and the **`export`** prefix modifier on any existing decl
    // (`DExport`, a wrapper variant that renders as `export ` + the
    // inner decl's pretty form). The driver gains two import decls, two
    // tag decls, and one exported fn decl on top of the Phase 7.5d
    // baseline:
    //   "module Examples.Bigger\n
    //    import Std.List\n
    //    import Std.Maybe\n
    //    tag UserId\n
    //    tag Email\n
    //    type Maybe A = Some A | None\n
    //    type Color = Red | Green | Blue\n
    //    type Container = MkBox Maybe[Int]\n
    //    let answer = 42\n
    //    let zero = 0\n
    //    let uid = \"user-42\"[UserId]\n
    //    export fn addOne(x Int) Int = x + 1\n
    //    fn double(x Int) Int = x * 2\n
    //    fn classify(x Int) Int = match x with | 0 -> 0 | _ -> 1\n
    //    fn pickColor(x Int) Color = if (x) Red else Green\n
    //    fn shift(x Int) Int = let y = x + 1 in y * 2\n
    //    fn applyDouble(x Int) Int = (\\y. y * 2) x\n
    //    fn greet() Str = \"hello\"\n
    //    fn classifyXs(xs Int) Int = match xs with | [] -> 0 | h :: t -> 1"
    // and pretty-prints the whole module deterministically: the module
    // header on its own line, each import/tag/type/let/fn decl normalised
    // to the form used by 12/13/14 (ctor args in parens; fn params
    // space-separated; body expressions fully parenthesised; let decls as
    // `let name = expr`; match in expression position as `(match scrut | p -> e | ...)`; let-in chains as `(let name = e1 in e2)`;
    // lambdas as `(fun x -> e)`; string literals rendered with
    // surrounding quotes as `"<s>"`; nil patterns as `[]`; cons patterns
    // as `(h :: t)`; tagged literals as `(<lit>[Tag])`; parametric ctor
    // args as `<Head>[<arg>]`; tag decls as `tag <Name>`; import decls as
    // `import <Dot.Path>`; exported decls as `export <show inner decl>`).
    let expected =
        [ "module Examples.Bigger"
          "import Std.List"
          "import Std.Maybe"
          "tag UserId"
          "tag Email"
          "type Maybe (A) = Some(A) | None"
          "type Color = Red | Green | Blue"
          "type Container = MkBox(Maybe[Int])"
          "let answer = 42"
          "let zero = 0"
          "let uid = (\"user-42\"[UserId])"
          "export fn addOne (x: Int) -> Int = (x + 1)"
          "fn double (x: Int) -> Int = (x * 2)"
          "fn classify (x: Int) -> Int = (match x | 0 -> 0 | _ -> 1)"
          "fn pickColor (x: Int) -> Color = (if x Red else Green)"
          "fn shift (x: Int) -> Int = (let y = (x + 1) in (y * 2))"
          "fn applyDouble (x: Int) -> Int = ((fun y -> (y * 2)) x)"
          "fn greet () -> Str = \"hello\""
          "fn classifyXs (xs: Int) -> Int = (match xs | [] -> 0 | (h :: t) -> 1)" ]
    for line in expected do
        Assert.True(
            stdout.Contains(line),
            $"missing pretty form: {line}\nstdout: {stdout}\nstderr: {stderr}")
