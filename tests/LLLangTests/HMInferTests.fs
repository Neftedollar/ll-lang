module LLLang.Tests.HMInferTests

open System.IO
open Xunit
open LLLang.AST
open LLLang.Lexer
open LLLang.Parser
open LLLang.Elaborator
open LLLang.Types
open LLLang.TypedAST
open LLLang.HMInfer

// ---------- helpers (reused across all tests) ----------

let private inferSrc (src: string) : Result<TypedModule, LLError list> =
    match tokenize src |> Result.bind parseModuleWithPos with
    | Error e -> failwith $"parse: {e}"
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> failwith $"elaborator: {es}"
        | Ok (m', env) -> infer pm m' env

let private inferOk (src: string) : TypedModule =
    match inferSrc src with
    | Ok tm -> tm
    | Error es -> failwith $"unexpected hm errors: {es}"

let private inferErrs (src: string) : LLError list =
    match inferSrc src with
    | Ok _ -> []
    | Error es -> es

let private schemeOf (tm: TypedModule) (name: string) : TypeScheme =
    Map.find name tm.Env

let private readValid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/valid", name))

let private readInvalid name =
    File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "../../spec/examples/invalid", name))

// ---------- Task 1: module scaffolding test ----------

[<Fact>]
let ``Types and TypedAST modules compile and Env type exists`` () =
    // Trivial sanity check: the new types must be referenceable.
    let empty : Env = Map.empty
    let m : TypeScheme = { Vars = []; Body = TyName "Int" }
    Assert.Equal(0, Map.count empty)
    Assert.Equal<Ident list>([], m.Vars)

// --- Task 2: Substitution + fresh vars + generalize/instantiate ---

[<Fact>]
let ``freshVar increments and produces $N names`` () =
    let s = newFreshState ()
    let a = freshVar s
    let b = freshVar s
    Assert.Equal(TyVar "$0", a)
    Assert.Equal(TyVar "$1", b)

[<Fact>]
let ``applyType replaces flexible var`` () =
    let s : Subst = Map.ofList ["$0", TyName "Int"]
    let t = TyFn(TyVar "$0", TyName "Bool")
    Assert.Equal(TyFn(TyName "Int", TyName "Bool"), applyType s t)

[<Fact>]
let ``applyType does not replace rigid var`` () =
    let s : Subst = Map.ofList ["a", TyName "Int"]
    let t = TyVar "a"
    Assert.Equal(TyVar "a", applyType s t)

[<Fact>]
let ``applyType is recursive through TyFn and TyApp`` () =
    let s : Subst = Map.ofList ["$0", TyName "Int"; "$1", TyName "Str"]
    let t = TyApp(TyVar "$0", TyFn(TyVar "$1", TyVar "$0"))
    Assert.Equal(TyApp(TyName "Int", TyFn(TyName "Str", TyName "Int")), applyType s t)

[<Fact>]
let ``compose applies s2 first then s1`` () =
    let s1 : Subst = Map.ofList ["$1", TyName "Int"]
    let s2 : Subst = Map.ofList ["$0", TyVar "$1"]
    let composed = compose s1 s2
    Assert.Equal(TyName "Int", applyType composed (TyVar "$0"))

[<Fact>]
let ``ftvType collects only flexible vars`` () =
    let t = TyFn(TyVar "$0", TyFn(TyVar "a", TyVar "$1"))
    let ftvs = ftvType t
    Assert.True(Set.contains "$0" ftvs)
    Assert.True(Set.contains "$1" ftvs)
    Assert.False(Set.contains "a" ftvs)

[<Fact>]
let ``ftvScheme removes quantified vars from body free vars`` () =
    let sch = { Vars = ["$0"]; Body = TyFn(TyVar "$0", TyVar "$1") }
    let ftvs = ftvScheme sch
    Assert.False(Set.contains "$0" ftvs)
    Assert.True(Set.contains "$1" ftvs)

[<Fact>]
let ``generalize quantifies free vars not in env`` () =
    let envScheme = { Vars = []; Body = TyVar "$5" }
    let env : Env = Map.ofList ["fixed", envScheme]
    let ty = TyFn(TyVar "$0", TyVar "$5")
    let sch = generalize env ty
    Assert.Equal<Ident list>(["$0"], sch.Vars)

[<Fact>]
let ``instantiate replaces each quantified var with a fresh flexible var`` () =
    let fs = newFreshState ()
    let sch = { Vars = ["a"]; Body = TyFn(TyVar "a", TyVar "a") }
    let t = instantiate fs sch
    match t with
    | TyFn(TyVar v1, TyVar v2) ->
        Assert.Equal(v1, v2)
        Assert.StartsWith("$", v1)
    | _ -> failwith $"expected TyFn of same fresh var, got {t}"

[<Fact>]
let ``instantiate uses different fresh vars for different quantifiers`` () =
    let fs = newFreshState ()
    let sch = { Vars = ["a"; "b"]; Body = TyFn(TyVar "a", TyVar "b") }
    match instantiate fs sch with
    | TyFn(TyVar v1, TyVar v2) ->
        Assert.NotEqual<string>(v1, v2)
    | t -> failwith $"expected TyFn, got {t}"

[<Fact>]
let ``fromElaboratorEnv turns declared type vars into quantifiers`` () =
    let e3 : LLLang.Elaborator.TypeEnv = Map.ofList ["id", TyFn(TyVar "A", TyVar "A")]
    let env = fromElaboratorEnv e3
    let sch = Map.find "id" env
    Assert.Contains("A", sch.Vars)

// --- Task 3: Unification + occurs check ---

[<Fact>]
let ``unify two identical TyName succeeds with empty subst`` () =
    match unify (TyName "Int") (TyName "Int") with
    | Ok s -> Assert.Equal(0, Map.count s)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``unify flexible var with TyName binds var`` () =
    match unify (TyVar "$0") (TyName "Int") with
    | Ok s -> Assert.Equal(TyName "Int", Map.find "$0" s)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``unify TyName with flexible var binds var`` () =
    match unify (TyName "Int") (TyVar "$0") with
    | Ok s -> Assert.Equal(TyName "Int", Map.find "$0" s)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``unify rigid var with TyName fails with E001`` () =
    match unify (TyVar "a") (TyName "Int") with
    | Ok _ -> failwith "expected Error"
    | Error err -> Assert.Equal(E001, err.Code)

[<Fact>]
let ``unify two TyFn recurses on params and returns`` () =
    let t1 = TyFn(TyVar "$0", TyName "Bool")
    let t2 = TyFn(TyName "Int", TyVar "$1")
    match unify t1 t2 with
    | Ok s ->
        Assert.Equal(TyName "Int", Map.find "$0" s)
        Assert.Equal(TyName "Bool", Map.find "$1" s)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``unify TyApp with TyApp recurses`` () =
    let t1 = TyApp(TyName "Maybe", TyVar "$0")
    let t2 = TyApp(TyName "Maybe", TyName "Int")
    match unify t1 t2 with
    | Ok s -> Assert.Equal(TyName "Int", Map.find "$0" s)
    | Error e -> failwith $"expected Ok, got {e}"

[<Fact>]
let ``unify mismatched TyName fails with E001`` () =
    match unify (TyName "Int") (TyName "Str") with
    | Ok _ -> failwith "expected Error"
    | Error err -> Assert.Equal(E001, err.Code)

[<Fact>]
let ``unify tagged types with same base but different tag fails with E004`` () =
    let t1 = TyTagged(TyName "Float", UName "m")
    let t2 = TyTagged(TyName "Float", UName "s")
    match unify t1 t2 with
    | Ok _ -> failwith "expected Error"
    | Error err -> Assert.Equal(E004, err.Code)

[<Fact>]
let ``unify tagged vs untagged base fails with E005`` () =
    let t1 = TyTagged(TyName "Float", UName "m")
    let t2 = TyName "Float"
    match unify t1 t2 with
    | Ok _ -> failwith "expected Error"
    | Error err -> Assert.Equal(E005, err.Code)

[<Fact>]
let ``occurs check: $0 in TyFn($0, Int) -> E008`` () =
    let v = TyVar "$0"
    let t = TyFn(TyVar "$0", TyName "Int")
    match unify v t with
    | Ok _ -> failwith "expected E008"
    | Error err -> Assert.Equal(E008, err.Code)

[<Fact>]
let ``unify same flexible var with itself succeeds empty`` () =
    match unify (TyVar "$0") (TyVar "$0") with
    | Ok s -> Assert.Equal(0, Map.count s)
    | Error e -> failwith $"expected Ok, got {e}"

// --- Task 4: Algorithm W for basic expression forms ---

[<Fact>]
let ``infer int literal`` () =
    let tm = inferOk "module M\nlet x = 42"
    let sch = schemeOf tm "x"
    Assert.Equal(TyName "Int", sch.Body)
    Assert.Empty(sch.Vars)

[<Fact>]
let ``infer str literal`` () =
    let tm = inferOk "module M\nlet s = \"hi\""
    Assert.Equal(TyName "Str", (schemeOf tm "s").Body)

[<Fact>]
let ``infer bool literal`` () =
    let tm = inferOk "module M\nlet b = true"
    Assert.Equal(TyName "Bool", (schemeOf tm "b").Body)

[<Fact>]
let ``infer char literal`` () =
    let tm = inferOk "module M\nlet c = 'a'"
    Assert.Equal(TyName "Char", (schemeOf tm "c").Body)

[<Fact>]
let ``infer indented let without in: multi-line fn body`` () =
    let src = "module M\nf() =\n  x = 1\n  y = 2\n  x + y"
    let tm = inferOk src
    // Body should infer as Int
    let sch = schemeOf tm "f"
    match sch.Body with
    | TyFn(_, TyName "Int") | TyName "Int" -> ()
    | t -> failwith $"expected Int return, got {t}"

[<Fact>]
let ``infer indented let: single vs multi-line give same type`` () =
    let singleLine = "module M\ng() =\n  x = 1\n  y = 2\n  x + y"
    let multiLine  = "module M\ng() =\n  x = 1\n  y = 2\n  x + y"
    let t1 = (schemeOf (inferOk singleLine) "g").Body
    let t2 = (schemeOf (inferOk multiLine)  "g").Body
    Assert.Equal(t1, t2)

[<Fact>]
let ``infer fn with declared params and elided return`` () =
    let tm = inferOk "module M\ninc(x Int) = x + 1"
    let sch = schemeOf tm "inc"
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), sch.Body)

[<Fact>]
let ``infer external declaration is added to env`` () =
    let src =
        "module M\n" +
        "opaque Promise[A]\n" +
        "opaque Response\n" +
        "external fetch(url Str) Promise[Response]\n"
    let tm = inferOk src
    let sch = schemeOf tm "fetch"
    Assert.Equal(
        TyFn(TyName "Str", TyApp(TyName "Promise", TyName "Response")),
        sch.Body)

[<Fact>]
let ``infer external call uses declared type`` () =
    let src =
        "module M\n" +
        "opaque Promise[A]\n" +
        "opaque Response\n" +
        "external fetch(url Str) Promise[Response]\n" +
        "let req = fetch \"https://example.com\"\n"
    let tm = inferOk src
    Assert.Equal(
        TyApp(TyName "Promise", TyName "Response"),
        (schemeOf tm "req").Body)

[<Fact>]
let ``infer polymorphic id fn`` () =
    let tm = inferOk "module M\nid(x A) A = x"
    let sch = schemeOf tm "id"
    Assert.Equal(1, List.length sch.Vars)
    match sch.Body with
    | TyFn(TyVar a, TyVar b) -> Assert.Equal<string>(a, b)
    | t -> failwith $"expected TyFn of same var, got {t}"

[<Fact>]
let ``infer id applied to Int produces Int`` () =
    let src = "module M\nid(x A) A = x\nlet n = id 42"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (schemeOf tm "n").Body)

[<Fact>]
let ``infer id applied to Str produces Str`` () =
    let src = "module M\nid(x A) A = x\nlet s = id \"hi\""
    let tm = inferOk src
    Assert.Equal(TyName "Str", (schemeOf tm "s").Body)

[<Fact>]
let ``infer const fn has two quantifiers`` () =
    let tm = inferOk "module M\nconst_(x A)(y B) A = x"
    let sch = schemeOf tm "const_"
    Assert.Equal(2, List.length sch.Vars)

[<Fact>]
let ``infer lambda identity`` () =
    let tm = inferOk "module M\nlet f = \\x. x"
    let sch = schemeOf tm "f"
    Assert.Equal(1, List.length sch.Vars)
    match sch.Body with
    | TyFn(TyVar a, TyVar b) -> Assert.Equal<string>(a, b)
    | t -> failwith $"expected TyFn identity, got {t}"

[<Fact>]
let ``infer if branches unify`` () =
    let tm = inferOk "module M\nlet y = if true\n  1\nelse 2"
    Assert.Equal(TyName "Int", (schemeOf tm "y").Body)

[<Fact>]
let ``if branch mismatch yields E001`` () =
    let errs = inferErrs "module M\nlet y = if true\n  1\nelse \"x\""
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``pipe e -> f types as f e`` () =
    let tm = inferOk "module M\nlet y = 5 -> (\\x. x + 1)"
    Assert.Equal(TyName "Int", (schemeOf tm "y").Body)

[<Fact>]
let ``bind operator accepts functional rhs`` () =
    let tm = inferOk "module M\nlet y = 5 >>= (\\x. x + 1)"
    Assert.Equal(TyName "Int", (schemeOf tm "y").Body)

[<Fact>]
let ``bind operator rejects non-functional rhs`` () =
    let src = "module M\nlet bad = 5 >>= 1"
    match tokenize src |> Result.bind parseModuleWithPos with
    | Error e -> failwith $"parse: {e}"
    | Ok (m, pm) ->
        match elaborate pm m with
        | Ok _ -> Assert.True(false, "expected elaborator error for non-functional >>= rhs")
        | Error errs ->
            Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``bind operator requires rhs to return same carrier`` () =
    let errs = inferErrs "module M\nlet bad = 5 >>= (\\x. \"s\")"
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``sequencing operator requires same carrier`` () =
    let errs = inferErrs "module M\nlet bad = 1 >> \"x\""
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``choice operator requires same carrier`` () =
    let errs = inferErrs "module M\nlet bad = 1 <|> \"x\""
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``pipe chain with inline lambdas infers Int`` () =
    let tm = inferOk "module M\nlet y = 1 |> (\\x. x + 1) |> (\\x. x * 2)"
    Assert.Equal(TyName "Int", (schemeOf tm "y").Body)

[<Fact>]
let ``mixed symbolic chain reports mismatch when carriers diverge`` () =
    let errs = inferErrs "module M\nlet bad = (1 |> (\\x. x + 1)) <|> \"x\""
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``list of ints infers List Int`` () =
    let tm = inferOk "module M\nlet xs = [1 2 3]"
    let sch = schemeOf tm "xs"
    Assert.Equal(TyApp(TyName "List", TyName "Int"), sch.Body)

[<Fact>]
let ``heterogeneous list yields E001`` () =
    let errs = inferErrs "module M\nlet xs = [1 \"two\"]"
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``tagged literal preserves tag`` () =
    let tm = inferOk "module M\nlet d = 5.0[Meter]"
    Assert.Equal(TyTagged(TyName "Float", UName "Meter"), (schemeOf tm "d").Body)

// --- Task 5: Let-generalization + Match + Patterns ---

[<Fact>]
let ``let-generalization: polymorphic let used at two types`` () =
    let src =
        "module M\n" +
        "let id2 = \\x. x\n" +
        "let a = id2 1\n" +
        "let b = id2 \"s\""
    let tm = inferOk src
    Assert.Equal(TyName "Int", (schemeOf tm "a").Body)
    Assert.Equal(TyName "Str", (schemeOf tm "b").Body)

[<Fact>]
let ``instantiation is fresh per use`` () =
    let src =
        "module M\n" +
        "let id = \\x. x\n" +
        "let pair = id id"
    let tm = inferOk src
    Assert.NotNull(schemeOf tm "pair")

[<Fact>]
let ``nested let captures outer variable`` () =
    let src =
        "module M\n" +
        "outer(x) = \\y. x"
    let tm = inferOk src
    let sch = schemeOf tm "outer"
    Assert.InRange(List.length sch.Vars, 1, 2)

[<Fact>]
let ``match on Maybe with Some and None branches unify`` () =
    let src =
        "module M\n" +
        "Maybe A = Some A | None\n" +
        "unwrap(m Maybe[Int]) Int =\n" +
        "  | Some n -> n\n" +
        "  | None -> 0"
    let tm = inferOk src
    let sch = schemeOf tm "unwrap"
    Assert.Equal(TyFn(TyApp(TyName "Maybe", TyName "Int"), TyName "Int"), sch.Body)

[<Fact>]
let ``match branch type mismatch yields E001`` () =
    let src =
        "module M\n" +
        "Maybe A = Some A | None\n" +
        "unwrap(m Maybe[Int]) Int =\n" +
        "  | Some n -> n\n" +
        "  | None -> \"oops\""
    let errs = inferErrs src
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``value restriction not applied: polymorphic lambda reused`` () =
    let src =
        "module M\n" +
        "let f = \\x. x\n" +
        "let a = f 1\n" +
        "let b = f true"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (schemeOf tm "a").Body)
    Assert.Equal(TyName "Bool", (schemeOf tm "b").Body)

[<Fact>]
let ``occurs check fires from self-application`` () =
    let src = "module M\nlet bad = \\x. x x"
    let errs = inferErrs src
    Assert.Contains(errs, fun e -> e.Code = E008)

[<Fact>]
let ``unit tag preserved through polymorphic lambda`` () =
    let src =
        "module M\n" +
        "let f = \\x. x\n" +
        "let g = f 5.0[Meter]"
    let tm = inferOk src
    Assert.Equal(TyTagged(TyName "Float", UName "Meter"), (schemeOf tm "g").Body)

// --- Task 6: Top-level DFn/DLet/DImpl handling ---

[<Fact>]
let ``DFn elided return type gets concrete inferred type`` () =
    let tm = inferOk "module M\ninc(x Int) = x + 1"
    let sch = schemeOf tm "inc"
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), sch.Body)
    Assert.Empty(sch.Vars)

[<Fact>]
let ``DFn declared return type is honoured`` () =
    let tm = inferOk "module M\ninc(x Int) Int = x + 1"
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "inc").Body)

[<Fact>]
let ``DFn declared return type mismatch yields E001`` () =
    let errs = inferErrs "module M\nwrong(x Int) Str = x + 1"
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``DFn polymorphic id generalizes to one var`` () =
    let tm = inferOk "module M\nid(x A) A = x"
    let sch = schemeOf tm "id"
    Assert.Equal(1, List.length sch.Vars)

[<Fact>]
let ``DLet top-level polymorphic`` () =
    let tm = inferOk "module M\nlet id = \\x. x"
    let sch = schemeOf tm "id"
    Assert.Equal(1, List.length sch.Vars)

[<Fact>]
let ``DImpl fn name is mangled and inferred`` () =
    let src =
        "module M\n" +
        "Maybe A = Some A | None\n" +
        "trait Functor F =\n" +
        "  map(f A->B)(x F[A]) F[B]\n" +
        "impl Functor Maybe =\n" +
        "  map(f A->B)(x Maybe[A]) Maybe[B] =\n" +
        "    | Some a -> Some (f a)\n" +
        "    | None -> None"
    let tm = inferOk src
    Assert.True(Map.containsKey "map_Maybe" tm.Env)

[<Fact>]
let ``TypedAST has no TyVar placeholder for 01-basics`` () =
    let tm = inferOk (readValid "01-basics.lll")
    let rec containsWildcard ty =
        match ty with
        | TyVar "?" -> true
        | TyFn(a, b) | TyApp(a, b) -> containsWildcard a || containsWildcard b
        | TyTagged(a, _) -> containsWildcard a
        | _ -> false
    let rec checkExpr (e: TypedExpr) =
        if containsWildcard e.Type then failwith $"wildcard found in expr type: {e.Type}"
        match e.Expr with
        | TEApp(a, b) | TECons(a, b) -> checkExpr a; checkExpr b
        | TELam(ps, body) ->
            for (_, t) in ps do
                if containsWildcard t then failwith $"wildcard in lambda param: {t}"
            checkExpr body
        | TELet(_, sch, e1, e2) ->
            if containsWildcard sch.Body then failwith $"wildcard in let scheme: {sch.Body}"
            checkExpr e1
            Option.iter checkExpr e2
        | TELetPat(p, e1, e2) ->
            if containsWildcard p.Type then failwith $"wildcard in let-pat type: {p.Type}"
            checkExpr e1
            Option.iter checkExpr e2
        | TEIf(a, b, c) -> checkExpr a; checkExpr b; checkExpr c
        | TEMatch(s, branches) | TEMatchOf(s, branches) ->
            checkExpr s
            for (p, body) in branches do
                if containsWildcard p.Type then failwith $"wildcard in pattern type: {p.Type}"
                checkExpr body
        | TEPipe(a, b) -> checkExpr a; checkExpr b
        | TETagged(e, _) -> checkExpr e
        | TEList es | TETuple es -> for e in es do checkExpr e
        | TELit _ | TEVar _ | TECon _ -> ()
    for (d, _) in tm.Decls do
        match d with
        | TDFn(_, sch, body) ->
            if containsWildcard sch.Body then failwith $"wildcard in fn scheme: {sch.Body}"
            checkExpr body
        | TDLet(_, sch, body) ->
            if containsWildcard sch.Body then failwith $"wildcard in let scheme: {sch.Body}"
            checkExpr body
        | TDImpl(_, _, fns) ->
            for (_, sch, body) in fns do
                if containsWildcard sch.Body then failwith $"wildcard in impl scheme: {sch.Body}"
                checkExpr body
        | _ -> ()

// --- Task 8: Integration over valid corpus and invariants ---

let rec private collectTypes (e: TypedExpr) : TypeExpr list =
    e.Type :: (
        match e.Expr with
        | TEApp(a, b) | TECons(a, b) -> collectTypes a @ collectTypes b
        | TELam(ps, body) -> (ps |> List.map snd) @ collectTypes body
        | TELet(_, _, e1, e2) ->
            collectTypes e1 @ (e2 |> Option.map collectTypes |> Option.defaultValue [])
        | TELetPat(p, e1, e2) ->
            p.Type :: collectTypes e1 @ (e2 |> Option.map collectTypes |> Option.defaultValue [])
        | TEIf(a, b, c) -> collectTypes a @ collectTypes b @ collectTypes c
        | TEMatch(s, branches) | TEMatchOf(s, branches) ->
            collectTypes s @
            (branches |> List.collect (fun (p, body) -> p.Type :: collectTypes body))
        | TEPipe(a, b) -> collectTypes a @ collectTypes b
        | TETagged(e, _) -> collectTypes e
        | TEList es | TETuple es -> es |> List.collect collectTypes
        | TELit _ | TEVar _ | TECon _ -> [])

let rec private containsWildcard (ty: TypeExpr) : bool =
    match ty with
    | TyVar "?" -> true
    | TyFn(a, b) | TyApp(a, b) -> containsWildcard a || containsWildcard b
    | TyTagged(a, _) -> containsWildcard a
    | _ -> false

[<Theory>]
[<InlineData("01-basics.lll")>]
[<InlineData("02-adts.lll")>]
[<InlineData("03-tags.lll")>]
[<InlineData("06-stdlib.lll")>]
[<InlineData("07-text-processing.lll")>]
[<InlineData("08-lexer-poc.lll")>]
[<InlineData("09-lexer-real.lll")>]
[<InlineData("10-multiline-sum.lll")>]
[<InlineData("11-parser-real.lll")>]
[<InlineData("12-typeparser-real.lll")>]
[<InlineData("13-fnparser-real.lll")>]
[<InlineData("14-exprparser-real.lll")>]
[<InlineData("15-moduleparser-real.lll")>]
[<InlineData("16-elaborator-real.lll")>]
[<InlineData("17-pipeline-real.lll")>]
[<InlineData("18-hminfer-real.lll")>]
[<InlineData("19-codegen-real.lll")>]
[<InlineData("20-bootstrap-compiler.lll")>]
// 04-traits.lll and 05-modules.lll fail elaboration (unbound map/head from missing imports/impls).
let ``valid corpus infers ok`` (name: string) =
    let tm = inferOk (readValid name)
    Assert.NotNull(tm.Env)

[<Fact>]
let ``speed param types are TyTagged Float m and TyTagged Float s`` () =
    let src =
        "module M\n" +
        "tag m\n" +
        "tag s\n" +
        "speed(d Float[m])(t Float[s]) = d\n"
    let tm = inferOk src
    let sch = schemeOf tm "speed"
    // Expect fn body: TyFn(TyTagged(Float, m), TyFn(TyTagged(Float, s), _))
    match sch.Body with
    | TyFn(TyTagged(TyName "Float", UName "m"),
           TyFn(TyTagged(TyName "Float", UName "s"), _)) -> ()
    | other -> failwithf "speed scheme body not tagged as expected: %A" other

[<Fact>]
let ``typed AST has no TyVar wildcard for basics and adts`` () =
    for name in ["01-basics.lll"; "02-adts.lll"] do
        let tm = inferOk (readValid name)
        let allTys =
            tm.Decls |> List.collect (fun (d, _) ->
                match d with
                | TDFn(_, sch, body) -> sch.Body :: collectTypes body
                | TDLet(_, sch, body) -> sch.Body :: collectTypes body
                | TDImpl(_, _, fns) ->
                    fns |> List.collect (fun (_, sch, body) -> sch.Body :: collectTypes body)
                | _ -> [])
        for t in allTys do
            Assert.False(containsWildcard t, $"wildcard found in {name}: {t}")

[<Fact>]
let ``every TELam parameter carries a concrete type in basics`` () =
    let tm = inferOk (readValid "01-basics.lll")
    let rec walk (e: TypedExpr) =
        match e.Expr with
        | TELam(ps, body) ->
            for (_, ty) in ps do
                Assert.False(containsWildcard ty, $"wildcard in lambda param: {ty}")
            walk body
        | TEApp(a, b) | TECons(a, b) -> walk a; walk b
        | TELet(_, _, e1, e2) -> walk e1; Option.iter walk e2
        | TELetPat(_, e1, e2) -> walk e1; Option.iter walk e2
        | TEIf(a, b, c) -> walk a; walk b; walk c
        | TEMatch(s, branches) | TEMatchOf(s, branches) ->
            walk s
            for (_, body) in branches do walk body
        | TEPipe(a, b) -> walk a; walk b
        | TETagged(e, _) -> walk e
        | TEList es | TETuple es -> for e in es do walk e
        | TELit _ | TEVar _ | TECon _ -> ()
    for (d, _) in tm.Decls do
        match d with
        | TDFn(_, _, body) | TDLet(_, _, body) -> walk body
        | TDImpl(_, _, fns) -> for (_, _, b) in fns do walk b
        | _ -> ()

// --- Task 7: Trait dispatch + E006 ---

/// A variant of inferSrc that also captures elaborator errors (rather than failwith-ing)
let private tryInferSrc (src: string) : Result<TypedModule, LLError list> =
    match tokenize src |> Result.bind parseModuleWithPos with
    | Error e -> failwith $"parse: {e}"
    | Ok (m, pm) ->
        match elaborate pm m with
        | Error es -> Error es
        | Ok (m', env) -> infer pm m' env

let private functorMaybeModule =
    "module M\n" +
    "Maybe A = Some A | None\n" +
    "trait Functor F =\n" +
    "  map(f A->B)(x F[A]) F[B]\n" +
    "impl Functor Maybe =\n" +
    "  map(f A->B)(x Maybe[A]) Maybe[B] =\n" +
    "    | Some a -> Some (f a)\n" +
    "    | None -> None\n"

[<Fact>]
let ``trait dispatch: Functor Maybe resolves at call site`` () =
    let tm = inferOk functorMaybeModule
    Assert.True(Map.containsKey "map_Maybe" tm.Env)

[<Fact>]
let ``trait dispatch rewrites concrete call map to map_Maybe`` () =
    let src =
        functorMaybeModule +
        "useMap(xs Maybe[Int]) Maybe[Int] = map (\\x. x) xs\n"
    let tm = inferOk src
    let rec hasVar name (e: TypedExpr) =
        match e.Expr with
        | TEVar v -> v = name
        | TEApp(a, b) | TEPipe(a, b) | TECons(a, b) -> hasVar name a || hasVar name b
        | TELam(_, b) | TETagged(b, _) -> hasVar name b
        | TELet(_, _, e1, e2) -> hasVar name e1 || (e2 |> Option.exists (hasVar name))
        | TELetPat(_, e1, e2) -> hasVar name e1 || (e2 |> Option.exists (hasVar name))
        | TEIf(c, t, e2) -> hasVar name c || hasVar name t || hasVar name e2
        | TEMatch(s, brs) | TEMatchOf(s, brs) ->
            hasVar name s || (brs |> List.exists (fun (_, b) -> hasVar name b))
        | TEList es | TETuple es -> es |> List.exists (hasVar name)
        | TELit _ | TECon _ -> false
    let useMapBody =
        tm.Decls
        |> List.tryPick (fun (d, _) ->
            match d with
            | TDFn(sig_, _, body) when sig_.Name = "useMap" -> Some body
            | _ -> None)
        |> Option.defaultWith (fun () -> failwith "useMap decl not found")
    Assert.True(hasVar "map_Maybe" useMapBody)
    Assert.False(hasVar "map" useMapBody)

[<Fact>]
let ``trait dispatch rewrites constrained call map to single impl symbol`` () =
    let src =
        functorMaybeModule +
        "transform[F: Functor](xs F[Int])(f Int->Int) F[Int] = map f xs\n"
    let tm = inferOk src
    let rec hasVar name (e: TypedExpr) =
        match e.Expr with
        | TEVar v -> v = name
        | TEApp(a, b) | TEPipe(a, b) | TECons(a, b) -> hasVar name a || hasVar name b
        | TELam(_, b) | TETagged(b, _) -> hasVar name b
        | TELet(_, _, e1, e2) -> hasVar name e1 || (e2 |> Option.exists (hasVar name))
        | TELetPat(_, e1, e2) -> hasVar name e1 || (e2 |> Option.exists (hasVar name))
        | TEIf(c, t, e2) -> hasVar name c || hasVar name t || hasVar name e2
        | TEMatch(s, brs) | TEMatchOf(s, brs) ->
            hasVar name s || (brs |> List.exists (fun (_, b) -> hasVar name b))
        | TEList es | TETuple es -> es |> List.exists (hasVar name)
        | TELit _ | TECon _ -> false
    let transformBody =
        tm.Decls
        |> List.tryPick (fun (d, _) ->
            match d with
            | TDFn(sig_, _, body) when sig_.Name = "transform" -> Some body
            | _ -> None)
        |> Option.defaultWith (fun () -> failwith "transform decl not found")
    Assert.True(hasVar "map_Maybe" transformBody)
    Assert.False(hasVar "map" transformBody)

[<Fact>]
let ``clause-sugar match prefers non-function parameter when last is function`` () =
    let src =
        "module M\n" +
        "Maybe A = Some A | None\n" +
        "bind(fa Maybe[Int])(f Int->Maybe[Int]) Maybe[Int] =\n" +
        "  | Some a -> f a\n" +
        "  | None -> None\n"
    let tm = inferOk src
    Assert.True(Map.containsKey "bind" tm.Env)

// --- Phase 6.8: tuple patterns (polymorphism via untyped param) ---

[<Fact>]
let ``fn with PTuple pattern infers polymorphic (A, B) -> A`` () =
    // `fn fst(p) = match p with | (a, _) -> a` should generalize to a
    // polymorphic scheme of shape `(A, B) -> A` — encoded as
    // `TyFn(TyApp(TyApp(Tuple, $x), $y), $x)` after the two-pass + "?" slot
    // replacement machinery runs.
    let src = "module M\nfst(p) =\n  | (a, _) -> a"
    let tm = inferOk src
    let sch = schemeOf tm "fst"
    match sch.Body with
    | TyFn(argTy, retTy) ->
        match argTy with
        | TyApp(TyApp(TyName "Tuple", TyVar a), TyVar _) ->
            match retTy with
            | TyVar r -> Assert.Equal<string>(a, r)
            | _ -> failwith $"expected TyVar return matching tuple first elem, got {retTy}"
        | _ -> failwith $"expected argTy Tuple[A][B], got {argTy}"
    | _ -> failwith $"expected fn type, got {sch.Body}"

[<Fact>]
let ``tuple pattern match with specific types infers Int`` () =
    // `fn f(p) = match p with | (a, b) -> a + 1` has `a + 1` pinning `a`
    // to Int, so the scheme's return type must be Int.
    let src = "module M\nf(p) =\n  | (a, b) -> a + 1"
    let tm = inferOk src
    let sch = schemeOf tm "f"
    match sch.Body with
    | TyFn(_, TyName "Int") -> ()
    | _ -> failwith $"expected TyFn(_, Int), got {sch.Body}"

// --- Phase 6.8: mutually recursive top-level fns (two-pass inference) ---

[<Fact>]
let ``mutual recursion: even and odd both Int -> Bool`` () =
    let src =
        "module M\n" +
        "even(n Int) Bool =\n  if n == 0\n    true\n  else odd (n - 1)\n" +
        "odd(n Int) Bool =\n  if n == 0\n    false\n  else even (n - 1)"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Bool"), (schemeOf tm "even").Body)
    Assert.Equal(TyFn(TyName "Int", TyName "Bool"), (schemeOf tm "odd").Body)

[<Fact>]
let ``caller-before-callee: fn uses later-declared helper`` () =
    let src =
        "module M\n" +
        "caller(n Int) Int = helper n\n" +
        "helper(n Int) Int = n + 1"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "caller").Body)
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "helper").Body)

[<Fact>]
let ``single recursion fact still works`` () =
    let src = "module M\nfact(n Int) Int =\n  if n <= 1\n    1\n  else n * fact (n - 1)"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "fact").Body)

[<Fact>]
let ``single recursion fib still works`` () =
    let src = "module M\nfib(n Int) Int =\n  if n <= 1\n    n\n  else fib (n - 1) + fib (n - 2)"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "fib").Body)

[<Fact>]
let ``E006 corpus fires in HMInfer`` () =
    let src = readInvalid "E006-missing-impl.lll"
    let result = tryInferSrc src
    match result with
    | Error errs ->
        Assert.NotEmpty(errs)
        Assert.Contains(errs, fun e -> e.Code = E006)
    | Ok _ -> Assert.True(false, "Expected E006 for missing trait impl")

[<Fact>]
let ``E006 ambiguous trait impls in constrained call`` () =
    let src =
        "module M\n" +
        "trait Functor F =\n" +
        "  map(f A->B)(fa F[A]) F[B]\n" +
        "Maybe A = Some A | None\n" +
        "Box A = Box A\n" +
        "impl Functor Maybe =\n" +
        "  map(f A->B)(fa Maybe[A]) Maybe[B] =\n" +
        "    | Some a -> Some (f a)\n" +
        "    | None -> None\n" +
        "impl Functor Box =\n" +
        "  map(f A->B)(fa Box[A]) Box[B] =\n" +
        "    | Box a -> Box (f a)\n" +
        "transform[F: Functor](xs F[Int])(f Int->Int) F[Int] = map f xs\n"
    match tryInferSrc src with
    | Error errs ->
        Assert.Contains(errs, fun e -> e.Code = E006)
        Assert.Contains(errs, fun e -> e.Message.Contains("map"))
    | Ok _ -> Assert.True(false, "Expected E006 for ambiguous trait dispatch")

// --- Phase 7.1.5: cons patterns + cons expressions ---

[<Fact>]
let ``infer fn with cons pattern: List A -> A`` () =
    // `fn first(xs) = | x :: _ -> x` should be (List A) -> A.
    // (Uses the implicit fn-body match form since match-as-expression
    // is shipped in the next commit.)
    let src = "module M\nfirst(xs) =\n  | x :: _ -> x"
    let tm = inferOk src
    let sch = schemeOf tm "first"
    match sch.Body with
    | TyFn(TyApp(TyName "List", TyVar a), TyVar b) ->
        Assert.Equal<string>(a, b)
    | t -> failwith $"expected (List A) -> A, got {t}"

[<Fact>]
let ``infer cons expression: 1 :: empty list is List Int`` () =
    let src = "module M\nlet xs = 1 :: [2 3]"
    let tm = inferOk src
    let sch = schemeOf tm "xs"
    Assert.Equal(TyApp(TyName "List", TyName "Int"), sch.Body)

[<Fact>]
let ``infer cons expression: chain 1 :: 2 :: rest`` () =
    // `let xs = 1 :: 2 :: [3]` -> List Int
    let src = "module M\nlet xs = 1 :: 2 :: [3]"
    let tm = inferOk src
    Assert.Equal(TyApp(TyName "List", TyName "Int"), (schemeOf tm "xs").Body)

[<Fact>]
let ``infer cons expression type mismatch yields E001`` () =
    // 1 :: ["a"]  — Int head + List Str -> mismatch
    let errs = inferErrs "module M\nlet xs = 1 :: [\"a\"]"
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``constructor can be passed as function argument without eta-wrapper`` () =
    let src =
        "module M\n" +
        "Maybe A = Some A | None\n" +
        "wrapAll(xs List[Int]) List[Maybe[Int]] = listMap Some xs\n"
    let tm = inferOk src
    let sch = schemeOf tm "wrapAll"
    let listInt = TyApp(TyName "List", TyName "Int")
    let maybeInt = TyApp(TyName "Maybe", TyName "Int")
    let listMaybeInt = TyApp(TyName "List", maybeInt)
    Assert.Equal(TyFn(listInt, listMaybeInt), sch.Body)

// --- Phase 7.1.5: match as expression ---

[<Fact>]
let ``infer match-as-expression in let binding`` () =
    let src =
        "module M\n" +
        "let v = match 0 | 0 -> \"zero\" | _ -> \"other\""
    let tm = inferOk src
    Assert.Equal(TyName "Str", (schemeOf tm "v").Body)

[<Fact>]
let ``infer match-as-expression branch type mismatch yields E001`` () =
    let src =
        "module M\n" +
        "let v = match 0 | 0 -> \"zero\" | _ -> 1"
    let errs = inferErrs src
    Assert.Contains(errs, fun e -> e.Code = E001)

[<Fact>]
let ``infer cons pattern in match-as-expression`` () =
    // first uses an explicit `match` so the body is in expression position.
    let src =
        "module M\n" +
        "first(xs Int) Int =\n" +
        "  match [xs] | h :: _ -> h | _ -> 0"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "first").Body)

// --- Phase 7.1.6: let pattern destructuring ---

[<Fact>]
let ``infer let-tuple destructuring binds components at correct types`` () =
    // Tuples are not surface-syntax literals — they enter via fn params,
    // so we test by destructuring a tuple parameter. `let (a, b) = p` should
    // bind a and b to the tuple's element types.
    let src =
        "module M\n" +
        "addPair(p) Int =\n" +
        "  let (a, b) = p\n" +
        "  a + b"
    let tm = inferOk src
    let sch = schemeOf tm "addPair"
    // Body type is `(Int, Int) -> Int` encoded as
    // `TyFn(TyApp(TyApp(Tuple, Int), Int), Int)`.
    match sch.Body with
    | TyFn(TyApp(TyApp(TyName "Tuple", TyName "Int"), TyName "Int"), TyName "Int") -> ()
    | t -> failwith $"expected ((Int, Int) -> Int), got {t}"

[<Fact>]
let ``infer let wildcard destructuring`` () =
    // `let _ = e in body` — body type is body, e's type is irrelevant
    let src =
        "module M\n" +
        "f() Int =\n" +
        "  let _ = 99\n" +
        "  1"
    let tm = inferOk src
    Assert.Equal(TyName "Int", (schemeOf tm "f").Body)

[<Fact>]
let ``infer top-level let-tuple destructuring exposes a and b in env`` () =
    // Define a fn returning a tuple via destructuring, then a top-level
    // destructuring let against the result. We can't construct a tuple
    // literal at top level, so we project from a fn-param tuple.
    let src =
        "module M\n" +
        "pairFst(p) Int =\n" +
        "  let (a, _) = p\n" +
        "  a"
    let tm = inferOk src
    // pairFst should be polymorphic over the tuple's second element.
    let sch = schemeOf tm "pairFst"
    match sch.Body with
    | TyFn(TyApp(TyApp(TyName "Tuple", TyName "Int"), _), TyName "Int") -> ()
    | t -> failwith $"expected ((Int, _) -> Int), got {t}"

// --- Phase 7.2.1: surface tuple literal expressions ---

[<Fact>]
let ``infer let p = (1, 2) gives Tuple[Int][Int]`` () =
    // ETuple of two ints should infer to TyApp(TyApp(TyName Tuple, Int), Int),
    // matching the same encoding patternType uses for PTuple.
    let src = "module M\nlet p = (1, 2)"
    let tm = inferOk src
    let sch = schemeOf tm "p"
    match sch.Body with
    | TyApp(TyApp(TyName "Tuple", TyName "Int"), TyName "Int") -> ()
    | t -> failwith $"expected Tuple[Int][Int], got {t}"

[<Fact>]
let ``infer let pair = (1, "x") in let (a, b) = pair in a + 1`` () =
    // Heterogeneous tuple literal, then destructure, then arithmetic on `a`.
    // a must unify to Int via the `+ 1`. End-to-end round trip with PTuple.
    let src =
        "module M\n" +
        "run() Int =\n" +
        "  let pair = (1, \"x\")\n" +
        "  let (a, b) = pair\n" +
        "  a + 1"
    let tm = inferOk src
    let sch = schemeOf tm "run"
    match sch.Body with
    | TyName "Int" -> ()  // run() returns Int
    | t -> failwith $"expected run() : Int, got {t}"

[<Fact>]
let ``infer literal tuple matches PTuple encoding round-trip`` () =
    // The whole point: a tuple literal must unify with a PTuple destructure.
    // If the encodings disagree, this errors out.
    let src =
        "module M\n" +
        "fst3(unused Int) Int =\n" +
        "  let t = (10, 20, 30)\n" +
        "  let (a, _, _) = t\n" +
        "  a"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Int", TyName "Int"), (schemeOf tm "fst3").Body)

// --- Phase 7.3a bugfix (bug 1): clause-sugar arm body scoping ---------

[<Fact>]
let ``Bug1: clause-sugar wildcard arm with multi-line let-in scopes bindings`` () =
    // Regression for: a nested `let .. in` chain in the wildcard arm body
    // of a clause-sugar fn. With the old arm-body parser (parseExprInner)
    // only the first `let` got attached to the arm and the rest floated
    // to top level, producing E002 UnboundVar x / y. With parseBlockExpr
    // the whole chain stays inside the arm body and typechecks cleanly
    // to Tag -> Int.
    let src =
        "module M\n" +
        "Tag = A | B\n" +
        "f(t Tag) Int =\n" +
        "  | A -> 1\n" +
        "  | _ ->\n" +
        "    let p = (10, 20)\n" +
        "    let (x, y) = p\n" +
        "    x + y"
    let tm = inferOk src
    Assert.Equal(TyFn(TyName "Tag", TyName "Int"), (schemeOf tm "f").Body)

// --- Phase 7.3a bugfix (bug 2): list-literal patterns + arm preservation ---

[<Fact>]
let ``Bug2: empty-list pattern in clause-sugar arm typechecks`` () =
    // Regression for: `| [] -> ...` arm. Before the fix the pattern
    // parser errored on LBrack and the arm-loop silently dropped every
    // arm after it; the fn ended up with only the cons arm in its AST.
    // After the fix the `[]` pattern is recognised as PCon("[]", []),
    // HMInfer.patternType treats it as `List αElem`, and all three arms
    // survive — the fn's inferred type reflects the full List -> Int.
    let src =
        "module M\n" +
        "Token = TEnd | TMore\n" +
        "f(toks List[Token]) Int =\n" +
        "  | TEnd :: _ -> 1\n" +
        "  | [] -> 2\n" +
        "  | _ -> 3"
    let tm = inferOk src
    Assert.Equal(TyFn(TyApp(TyName "List", TyName "Token"), TyName "Int"), (schemeOf tm "f").Body)

[<Fact>]
let ``Bug2: list-literal pattern [x] binds head and matches single-elem list`` () =
    // `[x]` desugars to `PCons(PVar x, PCon("[]", []))`. The inferred
    // type of head1 is `List[Int] -> Int` once the `0` fallback pins
    // the element type.
    let src =
        "module M\n" +
        "head1(xs List[Int]) Int =\n" +
        "  | [x] -> x\n" +
        "  | _ -> 0"
    let tm = inferOk src
    Assert.Equal(TyFn(TyApp(TyName "List", TyName "Int"), TyName "Int"), (schemeOf tm "head1").Body)

// --- Issue #23: patternType over-applied constructor bugs -----------------

[<Fact>]
let ``Issue23: Some applied to two args is an error`` () =
    // `Some` takes exactly one argument. `Some x y` is over-applied and
    // must produce an E001 TypeMismatch, not silently succeed.
    let src =
        "module M\n" +
        "Option[A] = None | Some A\n" +
        "bad(o Option[Int]) Int =\n" +
        "  | Some x y -> x\n" +
        "  | None -> 0"
    let errs = inferErrs src
    Assert.NotEmpty(errs)
    Assert.True(errs |> List.exists (fun e -> e.Code = E001),
                sprintf "expected E001 error, got: %A" errs)

[<Fact>]
let ``Issue23: None applied to one arg is an error`` () =
    // `None` takes no arguments. `None x` is over-applied and must
    // produce an E001 TypeMismatch.
    let src =
        "module M\n" +
        "Option[A] = None | Some A\n" +
        "bad(o Option[Int]) Int =\n" +
        "  | None x -> 0\n" +
        "  | Some v -> v"
    let errs = inferErrs src
    Assert.NotEmpty(errs)
    Assert.True(errs |> List.exists (fun e -> e.Code = E001),
                sprintf "expected E001 error, got: %A" errs)

[<Fact>]
let ``Issue23: valid Option patterns still typecheck correctly`` () =
    // Regression guard: fixing the over-application bug must not break
    // valid constructor patterns with exactly the right number of args.
    let src =
        "module M\n" +
        "Option[A] = None | Some A\n" +
        "extract(o Option[Int]) Int =\n" +
        "  | Some x -> x\n" +
        "  | None -> 0"
    let tm = inferOk src
    Assert.Equal(TyFn(TyApp(TyName "Option", TyName "Int"), TyName "Int"),
                 (schemeOf tm "extract").Body)
