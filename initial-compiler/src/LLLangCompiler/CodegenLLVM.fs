module LLLang.CodegenLLVM

open System
open System.Text
open System.Text.RegularExpressions
open LLLang.AST
open LLLang.Types
open LLLang.TypedAST

let private emitFloat (f: float) =
    let s = sprintf "%g" f
    if s.Contains(".") || s.Contains("e") || s.Contains("E") then s else s + ".0"

let private emitLit = function
    | LInt n -> string n
    | LFloat f -> emitFloat f
    | LBool b -> if b then "1" else "0"
    | LChar c -> string (int c)
    | LStr _ -> "null"

let rec private emitType (t: TypeExpr) =
    match t with
    | TyName "Int" -> "i64"
    | TyName "Float" -> "double"
    | TyName "Bool" -> "i1"
    | TyName "Char" -> "i8"
    | TyName "Str" -> "ptr"
    | TyName "Unit" -> "void"
    | TyApp(TyName "List", _) -> "ptr"
    | TyApp(TyName "Maybe", _) -> "ptr"
    | TyFn(_, _) -> "ptr"
    | TyTagged(inner, _) -> emitType inner
    | _ -> "ptr"

let private defaultValue (t: TypeExpr) =
    match emitType t with
    | "i64" -> "0"
    | "i1" -> "0"
    | "i8" -> "0"
    | "double" -> "0.0"
    | "void" -> ""
    | _ -> "null"

let private emitParam (idx: int) ((_, t): string * TypeExpr) =
    emitType t + " %arg" + string idx

type private ConstVal =
    | CInt of int64
    | CFloat of float
    | CBool of bool
    | CChar of int

let private litToConst = function
    | LInt n -> Some (CInt n)
    | LFloat f -> Some (CFloat f)
    | LBool b -> Some (CBool b)
    | LChar c -> Some (CChar (int c))
    | LStr _ -> None

let private tryAsBinOp (te: TypedExpr) : (string * TypedExpr * TypedExpr) option =
    match te.Expr with
    | TEApp(outer, right) ->
        match outer.Expr with
        | TEApp(inner, left) ->
            match inner.Expr with
            | TEVar op ->
                match op with
                | "+"
                | "-"
                | "*"
                | "/"
                | "=="
                | "!="
                | "<"
                | ">"
                | "<="
                | ">=" -> Some (op, left, right)
                | _ -> None
            | _ -> None
        | _ -> None
    | _ -> None

let private evalIntBin (op: string) (a: int64) (b: int64) : ConstVal option =
    match op with
    | "+" -> Some (CInt (a + b))
    | "-" -> Some (CInt (a - b))
    | "*" -> Some (CInt (a * b))
    | "/" ->
        if b = 0L then None
        else Some (CInt (a / b))
    | "==" -> Some (CBool (a = b))
    | "!=" -> Some (CBool (a <> b))
    | "<" -> Some (CBool (a < b))
    | ">" -> Some (CBool (a > b))
    | "<=" -> Some (CBool (a <= b))
    | ">=" -> Some (CBool (a >= b))
    | _ -> None

let private evalFloatBin (op: string) (a: float) (b: float) : ConstVal option =
    match op with
    | "+" -> Some (CFloat (a + b))
    | "-" -> Some (CFloat (a - b))
    | "*" -> Some (CFloat (a * b))
    | "/" -> Some (CFloat (a / b))
    | "==" -> Some (CBool (a = b))
    | "!=" -> Some (CBool (a <> b))
    | "<" -> Some (CBool (a < b))
    | ">" -> Some (CBool (a > b))
    | "<=" -> Some (CBool (a <= b))
    | ">=" -> Some (CBool (a >= b))
    | _ -> None

let private evalBoolBin (op: string) (a: bool) (b: bool) : ConstVal option =
    match op with
    | "==" -> Some (CBool (a = b))
    | "!=" -> Some (CBool (a <> b))
    | _ -> None

let rec private evalConst (te: TypedExpr) : ConstVal option =
    match te.Expr with
    | TELit l -> litToConst l
    | TETagged(e, _) -> evalConst e
    | _ ->
        match tryAsBinOp te with
        | None -> None
        | Some (op, a, b) ->
            match evalConst a, evalConst b with
            | Some (CInt x), Some (CInt y) -> evalIntBin op x y
            | Some (CFloat x), Some (CFloat y) -> evalFloatBin op x y
            | Some (CBool x), Some (CBool y) -> evalBoolBin op x y
            | _ -> None

let private constToLlvm (llvmTy: string) (cv: ConstVal) : string option =
    match llvmTy, cv with
    | "i64", CInt n -> Some (string n)
    | "i64", CChar c -> Some (string c)
    | "double", CFloat f -> Some (emitFloat f)
    | "i1", CBool b -> Some (if b then "1" else "0")
    | "i8", CChar c -> Some (string c)
    | _ -> None

type private EmitCtx = {
    mutable TempCounter: int
    mutable LabelCounter: int
    Instructions: ResizeArray<string>
    mutable VarEnv: Map<string, string * string>
    StringLits: Map<string, string * int>
    mutable NeedsRuntime: bool
    // Top-level nullary declarations (TDFn with no params, or TDLet).
    // A bare TEVar referencing one of these needs to be lowered to a
    // zero-arg call (`call <ty> @<name>()`), because the parser desugars
    // top-level `name = expr` as `DFn name() = expr`.
    NullaryGlobals: Set<string>
}

let private freshTmp (ctx: EmitCtx) : string =
    let n = ctx.TempCounter
    ctx.TempCounter <- n + 1
    "%t" + string n

let private freshLabel (ctx: EmitCtx) (prefix: string) : string =
    let n = ctx.LabelCounter
    ctx.LabelCounter <- n + 1
    prefix + "_" + string n

let private emitInstr (ctx: EmitCtx) (s: string) =
    ctx.Instructions.Add("  " + s)

let private emitLabel (ctx: EmitCtx) (s: string) =
    ctx.Instructions.Add(s + ":")

let private collectOptions (items: string option list) : string list option =
    let rec loop acc rest =
        match rest with
        | [] -> Some (List.rev acc)
        | None :: _ -> None
        | Some v :: xs -> loop (v :: acc) xs
    loop [] items

let private resolveCallee (ctx: EmitCtx) (fnName: string) : string =
    // If the callee name is bound in the local SSA environment (e.g. a
    // function-typed parameter or let-binding), emit it as an SSA register
    // holding a function pointer — `%arg0`, not `@f`. Otherwise treat it
    // as a global symbol reference.
    match Map.tryFind fnName ctx.VarEnv with
    | Some (_, ssaValue) -> ssaValue
    | None -> "@" + fnName

let private emitCall (ctx: EmitCtx) (retTy: string) (fnName: string) (argTys: string list) (argVals: string list) : string option =
    let args =
        List.zip argTys argVals
        |> List.map (fun (ty, value) -> ty + " " + value)
        |> String.concat ", "
    let callee = resolveCallee ctx fnName
    if retTy = "void" then
        emitInstr ctx ("call void " + callee + "(" + args + ")")
        Some "0"
    else
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = call " + retTy + " " + callee + "(" + args + ")")
        Some tmp

let private emitIntCmp (ctx: EmitCtx) (pred: string) (aVal: string) (bVal: string) : string =
    let tmp = freshTmp ctx
    emitInstr ctx (tmp + " = icmp " + pred + " i64 " + aVal + ", " + bVal)
    tmp

let private emitFloatCmp (ctx: EmitCtx) (pred: string) (aVal: string) (bVal: string) : string =
    let tmp = freshTmp ctx
    emitInstr ctx (tmp + " = fcmp " + pred + " double " + aVal + ", " + bVal)
    tmp

let private andI1 (ctx: EmitCtx) (a: string) (b: string) : string =
    if a = "0" || b = "0" then "0"
    elif a = "1" then b
    elif b = "1" then a
    else
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = and i1 " + a + ", " + b)
        tmp

let rec private coerceValue (ctx: EmitCtx) (fromTy: string) (toTy: string) (value: string) : string =
    if fromTy = toTy then value
    else
        match fromTy, toTy with
        | "i1", "i64" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = zext i1 " + value + " to i64")
            tmp
        | "i8", "i64" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = zext i8 " + value + " to i64")
            tmp
        | "double", "i64" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = fptosi double " + value + " to i64")
            tmp
        | "ptr", "i64" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = ptrtoint ptr " + value + " to i64")
            tmp
        | "i64", "i1" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = icmp ne i64 " + value + ", 0")
            tmp
        | "i64", "i8" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = trunc i64 " + value + " to i8")
            tmp
        | "i64", "double" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = sitofp i64 " + value + " to double")
            tmp
        | "i64", "ptr" ->
            let tmp = freshTmp ctx
            emitInstr ctx (tmp + " = inttoptr i64 " + value + " to ptr")
            tmp
        | "i1", "double" ->
            let i64v = coerceValue ctx "i1" "i64" value
            coerceValue ctx "i64" "double" i64v
        | "i8", "double" ->
            let i64v = coerceValue ctx "i8" "i64" value
            coerceValue ctx "i64" "double" i64v
        | "ptr", "i1" ->
            let i64v = coerceValue ctx "ptr" "i64" value
            coerceValue ctx "i64" "i1" i64v
        | "ptr", "double" ->
            let i64v = coerceValue ctx "ptr" "i64" value
            coerceValue ctx "i64" "double" i64v
        | _, _ -> value

let private LIST_CONS_TAG = -1L

let mutable private ctorTagMap: Map<string, int64> = Map.empty
let mutable private nextCtorTag = 1L

// Lifted-lambda accumulator. Each `TELam` node encountered during emission
// synthesises a fresh top-level function `@__lam_N` (captureless lambda lifting)
// and pushes its LLVM text into `liftedLambdas`; `emitModule` appends the
// buffered definitions after the user's own decls. Module-level state mirrors
// the same pattern used for `ctorTagMap` — simplest thing that composes with
// the recursive expression emitter without threading through every helper.
let mutable private liftedLambdas: ResizeArray<string> = ResizeArray<string>()
let mutable private lambdaCounter = 0

let private resetLambdas () =
    // Only clear the per-module accumulator here; `lambdaCounter` is kept
    // monotonic across modules so multi-module project builds never produce
    // colliding `@__lam_N` symbols when several modules are concatenated.
    liftedLambdas <- ResizeArray<string>()

let private resetLambdaCounter () =
    lambdaCounter <- 0

let private freshLambdaName () =
    let n = lambdaCounter
    lambdaCounter <- n + 1
    "__lam_" + string n

let private resetCtorTags () =
    ctorTagMap <- Map.empty
    nextCtorTag <- 1L

let private ctorTag (name: string) : int64 =
    match Map.tryFind name ctorTagMap with
    | Some t -> t
    | None ->
        let t = nextCtorTag
        nextCtorTag <- nextCtorTag + 1L
        ctorTagMap <- Map.add name t ctorTagMap
        t

let private emitAllocNode (ctx: EmitCtx) (tagVal: string) (payloadI64: string) (tailPtr: string) : string =
    ctx.NeedsRuntime <- true
    let tmp = freshTmp ctx
    emitInstr ctx (tmp + " = call ptr @__ll_alloc(i64 " + tagVal + ", i64 " + payloadI64 + ", ptr " + tailPtr + ")")
    tmp

// Pattern-match lowering emits all three node-field loads BEFORE the
// conditional branch that checks `ptr != null`. This is safe only when the
// pointer is guaranteed non-null by a prior match arm (e.g. `| [] -> ...`
// handles the null case first). When a `PCons`/`PCon` arm is the first
// pattern and the scrutinee may be null (as with `match getArgs`), the
// load traps on NULL.
//
// Rather than refactor the match lowering into guarded blocks, we route the
// load through a tiny runtime helper (`node_tag_safe`) that returns a safe
// default when the pointer is NULL. The condition is still computed as
// `(nonNil AND tag==-1)`, which is now well-defined for either branch.
//
// The declare for `@node_tag_safe` / `@node_payload_safe` / `@node_tail_safe`
// is added by tools/llvm-add-declares.py (KNOWN_EXTERNALS), and the C runtime
// provides the bodies in sdks/Platform.LLVM.SDK/runtime/lllc_runtime.c.
let private emitLoadNodeTag (ctx: EmitCtx) (nodePtr: string) : string =
    let v = freshTmp ctx
    emitInstr ctx (v + " = call i64 @node_tag_safe(ptr " + nodePtr + ")")
    v

let private emitLoadNodePayload (ctx: EmitCtx) (nodePtr: string) : string =
    let v = freshTmp ctx
    emitInstr ctx (v + " = call i64 @node_payload_safe(ptr " + nodePtr + ")")
    v

let private emitLoadNodeTail (ctx: EmitCtx) (nodePtr: string) : string =
    let v = freshTmp ctx
    emitInstr ctx (v + " = call ptr @node_tail_safe(ptr " + nodePtr + ")")
    v

/// Collect all names bound by a pattern. Used by free-var analysis to
/// subtract pattern-introduced locals from the lambda's "outside" refs.
let rec private patternBinds (p: Pattern) : Set<string> =
    match p with
    | PVar x -> Set.singleton x
    | PWild -> Set.empty
    | PLit _ -> Set.empty
    | PCon(_, args) -> args |> List.fold (fun acc a -> Set.union acc (patternBinds a)) Set.empty
    | PCons(h, t) -> Set.union (patternBinds h) (patternBinds t)
    | PTuple items -> items |> List.fold (fun acc a -> Set.union acc (patternBinds a)) Set.empty

/// Collect all `TEVar` names referenced in `te` that are NOT shadowed by an
/// intervening binder inside the expression. The result is the set of names
/// the lambda body needs from its enclosing scope (locals + globals).
let private freeVars (te: TypedExpr) : Set<string> =
    let rec loop (bound: Set<string>) (e: TypedExpr) : Set<string> =
        match e.Expr with
        | TELit _ -> Set.empty
        | TECon _ -> Set.empty
        | TEVar x ->
            if Set.contains x bound then Set.empty
            else Set.singleton x
        | TEApp(f, a)
        | TEPipe(f, a)
        | TECons(f, a) -> Set.union (loop bound f) (loop bound a)
        | TELam(ps, body) ->
            let bound2 = ps |> List.fold (fun acc (n, _) -> Set.add n acc) bound
            loop bound2 body
        | TELet(x, _, e1, e2opt) ->
            let s1 = loop bound e1
            let bound2 = Set.add x bound
            let s2 =
                match e2opt with
                | Some e2 -> loop bound2 e2
                | None -> Set.empty
            Set.union s1 s2
        | TELetPat(tp, e1, e2opt) ->
            let s1 = loop bound e1
            let bound2 = Set.union bound (patternBinds tp.Pat)
            let s2 =
                match e2opt with
                | Some e2 -> loop bound2 e2
                | None -> Set.empty
            Set.union s1 s2
        | TEIf(c, t, el) ->
            Set.unionMany [ loop bound c; loop bound t; loop bound el ]
        | TEMatch(scrut, branches)
        | TEMatchOf(scrut, branches) ->
            let s0 = loop bound scrut
            branches
            |> List.fold (fun acc (tp, body) ->
                let bound2 = Set.union bound (patternBinds tp.Pat)
                Set.union acc (loop bound2 body)) s0
        | TETagged(inner, _) -> loop bound inner
        | TEList items
        | TETuple items ->
            items |> List.fold (fun acc a -> Set.union acc (loop bound a)) Set.empty
    loop Set.empty te

let private emitStringPtr (ctx: EmitCtx) (s: string) : string =
    match Map.tryFind s ctx.StringLits with
    | None -> "null"
    | Some (sym, len) ->
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = getelementptr inbounds ([" + string len + " x i8], ptr " + sym + ", i64 0, i64 0)")
        tmp

let rec private emitExprValue (ctx: EmitCtx) (te: TypedExpr) : string option =
    let wantedTy = emitType te.Type
    match tryAsBinOp te with
    | Some (op, a, b) ->
        match emitExprValue ctx a, emitExprValue ctx b with
        | Some aRaw, Some bRaw ->
            let lhsTy = emitType a.Type
            let rhsTy = emitType b.Type
            let opTy = if lhsTy = rhsTy then lhsTy else wantedTy
            let aVal = coerceValue ctx lhsTy opTy aRaw
            let bVal = coerceValue ctx rhsTy opTy bRaw
            match op with
            | "+"
            | "-"
            | "*"
            | "/" ->
                match opTy with
                | "i64" ->
                    match op with
                    | "+" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = add i64 " + aVal + ", " + bVal)
                        Some tmp
                    | "-" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = sub i64 " + aVal + ", " + bVal)
                        Some tmp
                    | "*" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = mul i64 " + aVal + ", " + bVal)
                        Some tmp
                    | "/" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = sdiv i64 " + aVal + ", " + bVal)
                        Some tmp
                    | _ -> None
                | "double" ->
                    match op with
                    | "+" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = fadd double " + aVal + ", " + bVal)
                        Some tmp
                    | "-" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = fsub double " + aVal + ", " + bVal)
                        Some tmp
                    | "*" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = fmul double " + aVal + ", " + bVal)
                        Some tmp
                    | "/" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = fdiv double " + aVal + ", " + bVal)
                        Some tmp
                    | _ -> None
                | _ -> None
            | "=="
            | "!="
            | "<"
            | ">"
            | "<="
            | ">=" ->
                match opTy with
                | "i64" ->
                    match op with
                    | "==" -> Some (emitIntCmp ctx "eq" aVal bVal)
                    | "!=" -> Some (emitIntCmp ctx "ne" aVal bVal)
                    | "<" -> Some (emitIntCmp ctx "slt" aVal bVal)
                    | ">" -> Some (emitIntCmp ctx "sgt" aVal bVal)
                    | "<=" -> Some (emitIntCmp ctx "sle" aVal bVal)
                    | ">=" -> Some (emitIntCmp ctx "sge" aVal bVal)
                    | _ -> None
                | "double" ->
                    match op with
                    | "==" -> Some (emitFloatCmp ctx "oeq" aVal bVal)
                    | "!=" -> Some (emitFloatCmp ctx "one" aVal bVal)
                    | "<" -> Some (emitFloatCmp ctx "olt" aVal bVal)
                    | ">" -> Some (emitFloatCmp ctx "ogt" aVal bVal)
                    | "<=" -> Some (emitFloatCmp ctx "ole" aVal bVal)
                    | ">=" -> Some (emitFloatCmp ctx "oge" aVal bVal)
                    | _ -> None
                | "i1" ->
                    match op with
                    | "==" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp eq i1 " + aVal + ", " + bVal)
                        Some tmp
                    | "!=" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp ne i1 " + aVal + ", " + bVal)
                        Some tmp
                    | _ -> None
                | "i8" ->
                    match op with
                    | "==" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp eq i8 " + aVal + ", " + bVal)
                        Some tmp
                    | "!=" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp ne i8 " + aVal + ", " + bVal)
                        Some tmp
                    | _ -> None
                | "ptr" ->
                    match op with
                    | "==" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp eq ptr " + aVal + ", " + bVal)
                        Some tmp
                    | "!=" ->
                        let tmp = freshTmp ctx
                        emitInstr ctx (tmp + " = icmp ne ptr " + aVal + ", " + bVal)
                        Some tmp
                    | _ -> None
                | _ -> None
            | _ ->
                match op with
                | "==" | "!=" ->
                    let ai64 = coerceValue ctx opTy "i64" aVal
                    let bi64 = coerceValue ctx opTy "i64" bVal
                    if op = "==" then Some (emitIntCmp ctx "eq" ai64 bi64)
                    else Some (emitIntCmp ctx "ne" ai64 bi64)
                | _ -> None
        | _ -> None
    | None ->
        match te.Expr with
        | TELit l ->
            match l with
            | LStr s -> Some (emitStringPtr ctx s)
            | _ -> Some (emitLit l)
        | TEVar name ->
            match Map.tryFind name ctx.VarEnv with
            | Some (fromTy, value) ->
                Some (coerceValue ctx fromTy wantedTy value)
            | None ->
                // Not a local SSA binding. If the reference is function-typed,
                // fall back to the global symbol — this lets callers pass a
                // top-level function as a first-class value (function pointer).
                let rec isFnTy t =
                    match t with
                    | TyFn(_, _) -> true
                    | TyTagged(inner, _) -> isFnTy inner
                    | _ -> false
                if isFnTy te.Type then Some ("@" + name)
                // Special case: `getArgs` is declared as a value of type
                // List[Str] in the elaborator, but at the LLVM level it's
                // implemented as a zero-arg runtime call `@ll_getArgs()`.
                // Emit the call so any `args = getArgs` or `match getArgs`
                // expression works. The declare for `ll_getArgs` is added
                // by the post-processor (tools/llvm-add-declares.py).
                elif name = "getArgs" then
                    let tmp = freshTmp ctx
                    emitInstr ctx (tmp + " = call ptr @ll_getArgs()")
                    Some tmp
                // Top-level bindings of the form `name = expr` are parsed as
                // zero-arg functions (DFn with no params) — so referencing
                // `msg` at a use site needs to emit a call `@msg()`. The
                // result type is whatever the function's body evaluated to;
                // the AST gives us that type on the TEVar node.
                // Restricted to `NullaryGlobals` to avoid lowering every
                // unresolved name (e.g. a lambda parameter missed by the
                // codegen, or a pattern bind) into a bogus global call.
                elif Set.contains name ctx.NullaryGlobals then
                    let tmp = freshTmp ctx
                    emitInstr ctx (tmp + " = call " + wantedTy + " @" + name + "()")
                    Some tmp
                else None
        | TETagged(inner, _) -> emitExprValue ctx inner
        | TECon c ->
            let tagV = string (ctorTag c)
            Some (emitAllocNode ctx tagV "0" "null")
        | TEList items ->
            let rec build (tail: string) (remaining: TypedExpr list) : string option =
                match remaining with
                | [] -> Some tail
                | x :: xs ->
                    match emitExprValue ctx x with
                    | None -> None
                    | Some xv ->
                        let xi64 = coerceValue ctx (emitType x.Type) "i64" xv
                        let node = emitAllocNode ctx (string LIST_CONS_TAG) xi64 tail
                        build node xs
            let revItems = List.rev items
            build "null" revItems
        | TETuple items ->
            // Tuple literals are lowered to fixed-length cons chains using the
            // same runtime node ABI as list values.
            let rec build (tail: string) (remaining: TypedExpr list) : string option =
                match remaining with
                | [] -> Some tail
                | x :: xs ->
                    match emitExprValue ctx x with
                    | None -> None
                    | Some xv ->
                        let xi64 = coerceValue ctx (emitType x.Type) "i64" xv
                        let node = emitAllocNode ctx (string LIST_CONS_TAG) xi64 tail
                        build node xs
            let revItems = List.rev items
            build "null" revItems
        | TECons(h, t) ->
            match emitExprValue ctx h, emitExprValue ctx t with
            | Some hv, Some tv ->
                let hi64 = coerceValue ctx (emitType h.Type) "i64" hv
                let tptr = coerceValue ctx (emitType t.Type) "ptr" tv
                Some (emitAllocNode ctx (string LIST_CONS_TAG) hi64 tptr)
            | _ -> None
        | TELet(x, _, e, Some body) ->
            match emitExprValue ctx e with
            | None -> None
            | Some ev ->
                let old = ctx.VarEnv
                let evTy = emitType e.Type
                ctx.VarEnv <- Map.add x (evTy, ev) old
                let out = emitExprValue ctx body
                ctx.VarEnv <- old
                out
        | TELet(x, _, e, None) ->
            match emitExprValue ctx e with
            | None -> None
            | Some ev ->
                let evTy = emitType e.Type
                ctx.VarEnv <- Map.add x (evTy, ev) ctx.VarEnv
                Some ev
        | TELetPat(tp, e, bodyOpt) ->
            // Elaborator maps `_ = rhs` and other pattern-LHS bindings to
            // TELetPat. Codegen must emit the RHS (for side effects) and
            // recurse into the body.
            //
            // Supported shapes:
            //   PVar   — bind name to RHS value (same as TELet)
            //   PWild  — evaluate RHS, drop
            //   PTuple — destructure an N-cell cons chain (same ABI as list
            //            cons: {tag=-1, payload, tail}); each sub-pattern
            //            binds to `payload` of the i-th cell. Nested PTuples
            //            recurse by walking into the payload as a ptr.
            //
            // Other pattern shapes (PCons/PCon in let position, or literal
            // patterns) fall through to "emit RHS, bind nothing" — those
            // aren't used by stdlib/self-hosting today.
            match emitExprValue ctx e with
            | None -> None
            | Some ev ->
                let evTy = emitType e.Type
                let old = ctx.VarEnv
                // Bind a single sub-pattern against `cellPtr` (a ptr to a
                // cons cell whose payload holds the value we want to bind).
                // Returns unit — mutates ctx.VarEnv. `PTuple` recurses by
                // first converting payload i64 -> ptr, then unpacking again.
                let rec bindSubPattern (cellPtr: string) (p: Pattern) : unit =
                    match p with
                    | PWild -> ()
                    | PVar n ->
                        let payload = emitLoadNodePayload ctx cellPtr
                        ctx.VarEnv <- Map.add n ("i64", payload) ctx.VarEnv
                    | PTuple inner ->
                        // Nested tuple: payload is (i64-encoded) ptr to
                        // another cons chain. Convert and recurse.
                        let payload = emitLoadNodePayload ctx cellPtr
                        let innerPtr = coerceValue ctx "i64" "ptr" payload
                        bindTuplePattern innerPtr inner
                    | _ ->
                        // PCons/PCon/PLit in let-destructure — not needed by
                        // any current .lll; skip binding (RHS side-effect is
                        // preserved via the outer `emitExprValue ev`).
                        ()
                // Walk the cons chain: pats.[0] -> payload(base),
                // pats.[1] -> payload(tail(base)),
                // pats.[2] -> payload(tail(tail(base))), etc.
                and bindTuplePattern (basePtr: string) (pats: Pattern list) : unit =
                    let rec walk (cur: string) (remaining: Pattern list) =
                        match remaining with
                        | [] -> ()
                        | [p] -> bindSubPattern cur p
                        | p :: rest ->
                            bindSubPattern cur p
                            let nextTail = emitLoadNodeTail ctx cur
                            walk nextTail rest
                    walk basePtr pats
                match tp.Pat with
                | PVar name ->
                    ctx.VarEnv <- Map.add name (evTy, ev) ctx.VarEnv
                | PWild -> ()
                | PTuple items ->
                    // RHS must be a ptr to the cons chain. `ev` from
                    // TETuple/TECall returning a tuple is already ptr, but
                    // be defensive via coerceValue.
                    let basePtr = coerceValue ctx evTy "ptr" ev
                    bindTuplePattern basePtr items
                | _ -> ()
                let out =
                    match bodyOpt with
                    | Some body -> emitExprValue ctx body
                    | None -> Some ev
                ctx.VarEnv <- old
                out
        | TEIf(c, t, e) ->
            match emitExprValue ctx c with
            | None -> None
            | Some cv ->
                let c1 = coerceValue ctx (emitType c.Type) "i1" cv
                let thenLbl = freshLabel ctx "if_then"
                let elseLbl = freshLabel ctx "if_else"
                let endLbl = freshLabel ctx "if_end"
                emitInstr ctx ("br i1 " + c1 + ", label %" + thenLbl + ", label %" + elseLbl)
                emitLabel ctx thenLbl
                let tvOpt = emitExprValue ctx t
                let tv =
                    match tvOpt with
                    | Some v -> coerceValue ctx (emitType t.Type) wantedTy v
                    | None -> defaultValue te.Type
                emitInstr ctx ("br label %" + endLbl)
                emitLabel ctx elseLbl
                let evOpt = emitExprValue ctx e
                let ev =
                    match evOpt with
                    | Some v -> coerceValue ctx (emitType e.Type) wantedTy v
                    | None -> defaultValue te.Type
                emitInstr ctx ("br label %" + endLbl)
                emitLabel ctx endLbl
                if wantedTy = "void" then Some "0"
                else
                    let phi = freshTmp ctx
                    emitInstr ctx (phi + " = phi " + wantedTy + " [ " + tv + ", %" + thenLbl + " ], [ " + ev + ", %" + elseLbl + " ]")
                    Some phi
        | TEMatch(scrut, branches)
        | TEMatchOf(scrut, branches) ->
            let rec tupleToListPattern (items: Pattern list) : Pattern =
                match items with
                | [] -> PCon("[]", [])
                | x :: xs -> PCons(x, tupleToListPattern xs)

            let rec emitPatternValue (valueTy: string) (value: string) (pat: Pattern) : string * Map<string, string * string> =
                match pat with
                | PWild -> ("1", Map.empty)
                | PVar x -> ("1", Map.ofList [ (x, (valueTy, value)) ])
                | PLit (LInt n) ->
                    let v = coerceValue ctx valueTy "i64" value
                    (emitIntCmp ctx "eq" v (string n), Map.empty)
                | PLit (LBool b) ->
                    let v = coerceValue ctx valueTy "i1" value
                    let lit = if b then "1" else "0"
                    let c = freshTmp ctx
                    emitInstr ctx (c + " = icmp eq i1 " + v + ", " + lit)
                    (c, Map.empty)
                | PLit (LChar ch) ->
                    let v = coerceValue ctx valueTy "i8" value
                    let c = freshTmp ctx
                    emitInstr ctx (c + " = icmp eq i8 " + v + ", " + string (int ch))
                    (c, Map.empty)
                | PLit (LFloat f) ->
                    let v = coerceValue ctx valueTy "double" value
                    (emitFloatCmp ctx "oeq" v (emitFloat f), Map.empty)
                | PLit (LStr lit) ->
                    // Use null-safe @strEq (runtime helper) rather than
                    // libc's @strcmp: pattern lowering may eagerly load
                    // the scrutinee value, which can be NULL if the arm
                    // is reached via a fall-through from a prior arm that
                    // didn't fire. `strEq(null, _)` returns 0 (no match)
                    // instead of trapping.
                    let v = coerceValue ctx valueTy "ptr" value
                    let litPtr = emitStringPtr ctx lit
                    let eq = freshTmp ctx
                    emitInstr ctx (eq + " = call i1 @strEq(ptr " + v + ", ptr " + litPtr + ")")
                    (eq, Map.empty)
                | PCon(name, args) ->
                    let p = coerceValue ctx valueTy "ptr" value
                    emitCtorPattern name args p
                | PCons(_, _) ->
                    let p = coerceValue ctx valueTy "ptr" value
                    emitListPattern pat p
                | PTuple items ->
                    let p = coerceValue ctx valueTy "ptr" value
                    emitListPattern (tupleToListPattern items) p

            and emitListPattern (pat: Pattern) (ptrVal: string) : string * Map<string, string * string> =
                match pat with
                | PWild -> ("1", Map.empty)
                | PVar x -> ("1", Map.ofList [ (x, ("ptr", ptrVal)) ])
                | PCon("[]", []) ->
                    let isNil = freshTmp ctx
                    emitInstr ctx (isNil + " = icmp eq ptr " + ptrVal + ", null")
                    (isNil, Map.empty)
                | PCons(h, t) ->
                    let nonNil = freshTmp ctx
                    emitInstr ctx (nonNil + " = icmp ne ptr " + ptrVal + ", null")
                    let tag = emitLoadNodeTag ctx ptrVal
                    let isCons = emitIntCmp ctx "eq" tag (string LIST_CONS_TAG)
                    let baseCond = andI1 ctx nonNil isCons
                    let payload = emitLoadNodePayload ctx ptrVal
                    let tail = emitLoadNodeTail ctx ptrVal
                    let (hc, hb) = emitPatternValue "i64" payload h
                    let (tc, tb) = emitPatternValue "ptr" tail t
                    let c1 = andI1 ctx baseCond hc
                    let c2 = andI1 ctx c1 tc
                    let merged = Map.fold (fun acc k v -> Map.add k v acc) hb tb
                    (c2, merged)
                | _ -> ("0", Map.empty)

            and emitCtorPattern (name: string) (args: Pattern list) (ptrVal: string) : string * Map<string, string * string> =
                if name = "[]" && List.isEmpty args then
                    emitListPattern (PCon(name, args)) ptrVal
                else
                    let nonNull = freshTmp ctx
                    emitInstr ctx (nonNull + " = icmp ne ptr " + ptrVal + ", null")
                    let tagV = emitLoadNodeTag ctx ptrVal
                    let tagEq = emitIntCmp ctx "eq" tagV (string (ctorTag name))
                    let baseCond = andI1 ctx nonNull tagEq
                    let restPtr = emitLoadNodeTail ctx ptrVal
                    let firstPayload = emitLoadNodePayload ctx ptrVal

                    let rec consume (accCond: string) (accBinds: Map<string, string * string>) (tailPtr: string) (idx: int) (rest: Pattern list) =
                        match rest with
                        | [] ->
                            let doneTail = freshTmp ctx
                            emitInstr ctx (doneTail + " = icmp eq ptr " + tailPtr + ", null")
                            (andI1 ctx accCond doneTail, accBinds)
                        | p :: ps ->
                            if idx = 0 then
                                let (pc, pb) = emitPatternValue "i64" firstPayload p
                                let cond2 = andI1 ctx accCond pc
                                let binds2 = Map.fold (fun st k v -> Map.add k v st) accBinds pb
                                consume cond2 binds2 tailPtr 1 ps
                            else
                                let nonNil = freshTmp ctx
                                emitInstr ctx (nonNil + " = icmp ne ptr " + tailPtr + ", null")
                                let tag = emitLoadNodeTag ctx tailPtr
                                let isCons = emitIntCmp ctx "eq" tag (string LIST_CONS_TAG)
                                let c0 = andI1 ctx accCond nonNil
                                let c1 = andI1 ctx c0 isCons
                                let payload = emitLoadNodePayload ctx tailPtr
                                let nextTail = emitLoadNodeTail ctx tailPtr
                                let (pc, pb) = emitPatternValue "i64" payload p
                                let c2 = andI1 ctx c1 pc
                                let b2 = Map.fold (fun st k v -> Map.add k v st) accBinds pb
                                consume c2 b2 nextTail (idx + 1) ps

                    if List.isEmpty args then
                        let emptyTail = freshTmp ctx
                        emitInstr ctx (emptyTail + " = icmp eq ptr " + restPtr + ", null")
                        (andI1 ctx baseCond emptyTail, Map.empty)
                    else
                        consume baseCond Map.empty restPtr 0 args

            let emitPatternMatch (scrutTy: string) (scrutVal: string) (tp: TypedPattern) : string * Map<string, string * string> =
                emitPatternValue scrutTy scrutVal tp.Pat

            match emitExprValue ctx scrut with
            | None -> None
            | Some scrutV ->
                let scrutTy = emitType scrut.Type
                let startLbl = freshLabel ctx "match_case"
                let endLbl = freshLabel ctx "match_end"
                let failLbl = freshLabel ctx "match_fail"
                let incoming = ResizeArray<string * string>()
                let mutable nextLabel = startLbl

                emitInstr ctx ("br label %" + startLbl)

                for i = 0 to (List.length branches - 1) do
                    let (pat, body) = branches.[i]
                    emitLabel ctx nextLabel
                    let (cond, binds) = emitPatternMatch scrutTy scrutV pat
                    let bodyLbl = freshLabel ctx "match_body"
                    let nextLbl =
                        if i = List.length branches - 1 then failLbl
                        else freshLabel ctx "match_next"
                    if cond = "1" then
                        emitInstr ctx ("br label %" + bodyLbl)
                    elif cond = "0" then
                        emitInstr ctx ("br label %" + nextLbl)
                    else
                        emitInstr ctx ("br i1 " + cond + ", label %" + bodyLbl + ", label %" + nextLbl)

                    emitLabel ctx bodyLbl
                    let oldEnv = ctx.VarEnv
                    let withBinds = Map.fold (fun acc k v -> Map.add k v acc) oldEnv binds
                    ctx.VarEnv <- withBinds
                    let bodyV =
                        match emitExprValue ctx body with
                        | Some v -> coerceValue ctx (emitType body.Type) wantedTy v
                        | None -> defaultValue te.Type
                    ctx.VarEnv <- oldEnv
                    incoming.Add((bodyV, bodyLbl))
                    emitInstr ctx ("br label %" + endLbl)
                    nextLabel <- nextLbl

                emitLabel ctx failLbl
                let fallback = defaultValue te.Type
                incoming.Add((fallback, failLbl))
                emitInstr ctx ("br label %" + endLbl)

                emitLabel ctx endLbl
                if wantedTy = "void" then Some "0"
                else
                    let phi = freshTmp ctx
                    let incomingText =
                        incoming
                        |> Seq.map (fun (v, lbl) -> "[ " + v + ", %" + lbl + " ]")
                        |> String.concat ", "
                    emitInstr ctx (phi + " = phi " + wantedTy + " " + incomingText)
                    Some phi
        | TEApp(fnExpr, argExpr) ->
            let rec gatherArgs head acc =
                match head.Expr with
                | TEApp(g, x) -> gatherArgs g (x :: acc)
                | _ -> (head, acc)
            let (head, args) = gatherArgs fnExpr [argExpr]
            match head.Expr with
            | TECon c ->
                let argValsOpt =
                    args
                    |> List.map (fun a -> emitExprValue ctx a |> Option.map (fun v -> coerceValue ctx (emitType a.Type) "i64" v))
                    |> collectOptions
                match argValsOpt with
                | None -> None
                | Some argI64s ->
                    let tagV = string (ctorTag c)
                    match argI64s with
                    | [] -> Some (emitAllocNode ctx tagV "0" "null")
                    | h :: rest ->
                        let rec buildRest tail xs =
                            match xs with
                            | [] -> tail
                            | x :: xr ->
                                let node = emitAllocNode ctx (string LIST_CONS_TAG) x tail
                                buildRest node xr
                        let tailList =
                            match rest with
                            | [] -> "null"
                            | _ ->
                                let rev = List.rev rest
                                buildRest "null" rev
                        Some (emitAllocNode ctx tagV h tailList)
            | TEVar fnName ->
                let argVals = args |> List.map (emitExprValue ctx) |> collectOptions
                match argVals with
                | None -> None
                | Some vals ->
                    let argTys = args |> List.map (fun a -> emitType a.Type)
                    emitCall ctx wantedTy fnName argTys vals
            | _ -> None
        | TELam(lamParams, body) ->
            // Captureless lambda lifting (Strategy A): synthesise a fresh
            // top-level LLVM function `@__lam_N` and evaluate to its address
            // as a function pointer (ptr). Works as long as the lambda body
            // does not reference any SSA-local binding from the enclosing
            // scope (globals and lambda's own params are fine).
            //
            // When a true capture is detected, we currently emit a null
            // pointer and record a comment — good enough to keep codegen
            // progressing; call sites of such lambdas will trap at runtime.
            // Full closure support (Strategy B: struct { fn_ptr, env }) is
            // future work.
            let paramNames = lamParams |> List.map fst |> Set.ofList
            let bodyFree = freeVars body
            let outsideRefs = Set.difference bodyFree paramNames
            let captures =
                outsideRefs
                |> Set.filter (fun n -> Map.containsKey n ctx.VarEnv)
            if not (Set.isEmpty captures) then
                // Document the capture-miss inline so the .ll output is
                // self-describing during this bring-up.
                let capList = captures |> Set.toList |> String.concat ","
                emitInstr ctx ("; NOTE: captureful lambda skipped (free: " + capList + ")")
                Some "null"
            else
                let lamName = freshLambdaName ()
                let retTy =
                    match te.Type with
                    | TyFn(_, r) -> emitType r
                    | _ -> emitType body.Type
                let argsSig =
                    lamParams
                    |> List.mapi emitParam
                    |> String.concat ", "
                let lamVarEnv =
                    lamParams
                    |> List.mapi (fun i (n, t) -> (n, (emitType t, "%arg" + string i)))
                    |> Map.ofList
                let lamCtx = {
                    TempCounter = 0
                    LabelCounter = 0
                    Instructions = ResizeArray<string>()
                    VarEnv = lamVarEnv
                    StringLits = ctx.StringLits
                    NeedsRuntime = false
                    NullaryGlobals = ctx.NullaryGlobals
                }
                let bodyVal = emitExprValue lamCtx body
                let instrBlock =
                    if lamCtx.Instructions.Count = 0 then ""
                    else "\n" + String.concat "\n" lamCtx.Instructions
                let defText =
                    if retTy = "void" then
                        "define void @" + lamName + "(" + argsSig + ") {\n" +
                        "entry:" + instrBlock + "\n" +
                        "  ret void\n" +
                        "}"
                    else
                        let retValue =
                            match bodyVal with
                            | Some v -> coerceValue lamCtx (emitType body.Type) retTy v
                            | None -> defaultValue body.Type
                        let instrBlock2 =
                            if lamCtx.Instructions.Count = 0 then ""
                            else "\n" + String.concat "\n" lamCtx.Instructions
                        "define " + retTy + " @" + lamName + "(" + argsSig + ") {\n" +
                        "entry:" + instrBlock2 + "\n" +
                        "  ret " + retTy + " " + retValue + "\n" +
                        "}"
                liftedLambdas.Add(defText)
                if lamCtx.NeedsRuntime then ctx.NeedsRuntime <- true
                Some ("@" + lamName)
        | _ -> None

let private emitCastToMainI32 (ctx: EmitCtx) (bodyTy: string) (value: string) : string =
    match bodyTy with
    | "i64" ->
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = trunc i64 " + value + " to i32")
        tmp
    | "i1" ->
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = zext i1 " + value + " to i32")
        tmp
    | "i8" ->
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = zext i8 " + value + " to i32")
        tmp
    | "double" ->
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = fptosi double " + value + " to i32")
        tmp
    | "i32" -> value
    | "ptr" ->
        let asI64 = coerceValue ctx "ptr" "i64" value
        let tmp = freshTmp ctx
        emitInstr ctx (tmp + " = trunc i64 " + asI64 + " to i32")
        tmp
    | _ -> "0"

let private emitRuntimeBlock () =
    // Minimal heap node runtime used by list/constructor lowering.
    // Layout: { tag: i64, payload: i64, tail: ptr }.
    "declare ptr @malloc(i64)\n\n" +
    "define ptr @__ll_alloc(i64 %tag, i64 %payload, ptr %tail) {\n" +
    "entry:\n" +
    "  %raw = call ptr @malloc(i64 24)\n" +
    "  %tagp = getelementptr inbounds { i64, i64, ptr }, ptr %raw, i32 0, i32 0\n" +
    "  store i64 %tag, ptr %tagp\n" +
    "  %payloadp = getelementptr inbounds { i64, i64, ptr }, ptr %raw, i32 0, i32 1\n" +
    "  store i64 %payload, ptr %payloadp\n" +
    "  %tailp = getelementptr inbounds { i64, i64, ptr }, ptr %raw, i32 0, i32 2\n" +
    "  store ptr %tail, ptr %tailp\n" +
    "  ret ptr %raw\n" +
    "}\n"

let private encodeCString (s: string) : string * int =
    let bytes = Encoding.UTF8.GetBytes(s)
    let sb = StringBuilder()
    for b in bytes do
        let ch = char b
        if b >= 32uy && b <= 126uy && ch <> '"' && ch <> '\\' then
            sb.Append(ch) |> ignore
        else
            sb.Append("\\").Append(b.ToString("X2")) |> ignore
    sb.Append("\\00") |> ignore
    (sb.ToString(), bytes.Length + 1)

let private collectPatternStringLits (pat: Pattern) : string list =
    let rec loop p =
        match p with
        | PLit (LStr s) -> [ s ]
        | PCon(_, args) -> args |> List.collect loop
        | PCons(h, t) -> (loop h) @ (loop t)
        | PTuple items -> items |> List.collect loop
        | _ -> []
    loop pat

let private collectExprStringLits (te: TypedExpr) : string list =
    let rec loop e =
        let fromNode =
            match e.Expr with
            | TELit (LStr s) -> [ s ]
            | _ -> []
        let fromChildren =
            match e.Expr with
            | TEApp(a, b)
            | TEPipe(a, b)
            | TECons(a, b) -> (loop a) @ (loop b)
            | TELam(_, body)
            | TETagged(body, _) -> loop body
            | TELet(_, _, e1, e2)
            | TELetPat(_, e1, e2) ->
                (loop e1) @ (e2 |> Option.map loop |> Option.defaultValue [])
            | TEIf(c, t, e2) ->
                (loop c) @ (loop t) @ (loop e2)
            | TEMatch(s, branches)
            | TEMatchOf(s, branches) ->
                let fromScrut = loop s
                let fromBranches =
                    branches
                    |> List.collect (fun (tp, body) -> (collectPatternStringLits tp.Pat) @ (loop body))
                fromScrut @ fromBranches
            | TEList es
            | TETuple es -> es |> List.collect loop
            | _ -> []
        fromNode @ fromChildren
    loop te

let private buildStringPool (tm: TypedModule) : Map<string, string * int> * string list =
    let allLits =
        tm.Decls
        |> List.collect (fun (decl, _) ->
            match decl with
            | TDFn(_, _, body) -> collectExprStringLits body
            | TDLet(_, _, e) -> collectExprStringLits e
            | TDImpl(_, _, methods) ->
                methods |> List.collect (fun (_, _, body) -> collectExprStringLits body)
            | _ -> [])
    let uniqueOrdered =
        let folder (seen: Set<string>, acc: string list) lit =
            if Set.contains lit seen then (seen, acc)
            else (Set.add lit seen, acc @ [lit])
        allLits |> List.fold folder (Set.empty, []) |> snd
    let indexed = uniqueOrdered |> List.mapi (fun i lit -> (i, lit))
    let map =
        indexed
        |> List.map (fun (i, lit) ->
            let sym = "@.str" + string i
            let (_, len) = encodeCString lit
            (lit, (sym, len)))
        |> Map.ofList
    let decls =
        indexed
        |> List.map (fun (i, lit) ->
            let sym = "@.str" + string i
            let (encoded, len) = encodeCString lit
            sym + " = private unnamed_addr constant [" + string len + " x i8] c\"" + encoded + "\", align 1")
    (map, decls)

let private emitGlobalStringPtrConst (stringPool: Map<string, string * int>) (s: string) : string option =
    match Map.tryFind s stringPool with
    | None -> None
    | Some (sym, len) ->
        Some ("getelementptr inbounds ([" + string len + " x i8], ptr " + sym + ", i64 0, i64 0)")

let private emitFn (stringPool: Map<string, string * int>) (nullaryGlobals: Set<string>) (sig_: TypedFnSig) (body: TypedExpr) =
    let retTy = emitType sig_.ReturnType
    let isMain = sig_.Name = "main" && List.isEmpty sig_.Params
    let name = if isMain then "main" else sig_.Name
    let args =
        sig_.Params
        |> List.mapi emitParam
        |> String.concat ", "
    let varEnv =
        sig_.Params
        |> List.mapi (fun i (paramName, paramTy) -> (paramName, (emitType paramTy, "%arg" + string i)))
        |> Map.ofList
    let ctx = {
        TempCounter = 0
        LabelCounter = 0
        Instructions = ResizeArray<string>()
        VarEnv = varEnv
        StringLits = stringPool
        NeedsRuntime = false
        NullaryGlobals = nullaryGlobals
    }
    let bodyValue = emitExprValue ctx body
    let instrBlock =
        if ctx.Instructions.Count = 0 then ""
        else "\n" + String.concat "\n" ctx.Instructions

    if retTy = "void" then
        ("define void @" + name + "(" + args + ") {\n" +
         "entry:" + instrBlock + "\n" +
         "  ret void\n" +
         "}",
         ctx.NeedsRuntime)
    elif isMain then
        let retValue =
            match bodyValue with
            | None -> "0"
            | Some v -> emitCastToMainI32 ctx (emitType body.Type) v
        let instrBlock2 =
            if ctx.Instructions.Count = 0 then ""
            else "\n" + String.concat "\n" ctx.Instructions
        ("define i32 @main() {\n" +
         "entry:" + instrBlock2 + "\n" +
         "  ret i32 " + retValue + "\n" +
         "}",
         ctx.NeedsRuntime)
    else
        let retValue =
            match bodyValue with
            | Some v -> coerceValue ctx (emitType body.Type) retTy v
            | None -> defaultValue sig_.ReturnType
        ("define " + retTy + " @" + name + "(" + args + ") {\n" +
         "entry:" + instrBlock + "\n" +
         "  ret " + retTy + " " + retValue + "\n" +
         "}",
         ctx.NeedsRuntime)

let private emitImplMethod (stringPool: Map<string, string * int>) (nullaryGlobals: Set<string>) (implType: string) ((sig_, _, body): TypedFnSig * TypeScheme * TypedExpr) =
    let sig2 = { sig_ with Name = sig_.Name + "_" + implType }
    emitFn stringPool nullaryGlobals sig2 body

let private emitDecl (stringPool: Map<string, string * int>) (nullaryGlobals: Set<string>) (decl: TypedDecl) =
    match decl with
    | TDOpaque(name, _) -> ("; opaque type " + name, false)
    | TDType(name, _, _) -> ("; type " + name + " (opaque in LLVM backend)", false)
    | TDExternal(sig_, _) ->
        let retTy = emitType sig_.ReturnType
        let args = sig_.Params |> List.map (snd >> emitType) |> String.concat ", "
        ("declare " + retTy + " @" + sig_.Name + "(" + args + ")", false)
    | TDFn(sig_, _, body) -> emitFn stringPool nullaryGlobals sig_ body
    | TDLet(name, sch, e) ->
        let ty = emitType sch.Body
        let value =
            match e.Expr with
            | TELit (LStr s) when ty = "ptr" ->
                match emitGlobalStringPtrConst stringPool s with
                | Some v -> v
                | None -> "null"
            | _ ->
                match evalConst e with
                | Some c ->
                    match constToLlvm ty c with
                    | Some v -> v
                    | None -> defaultValue sch.Body
                | None -> defaultValue sch.Body
        if ty = "void" then
            ("; let " + name + " : void", false)
        else
            ("@" + name + " = global " + ty + " " + value, false)
    | TDImpl(_, implType, methods) ->
        let emitted = methods |> List.map (emitImplMethod stringPool nullaryGlobals implType)
        let body =
            emitted
            |> List.map fst
            |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s))
            |> String.concat "\n\n"
        let needsRuntime = emitted |> List.exists snd
        (body, needsRuntime)
    | _ -> ("", false)

let private moduleName (path: string list) =
    if List.isEmpty path then "Anonymous" else String.concat "." path

// Nullary globals: top-level TDFn declarations with zero params. The parser
// desugars `name = expr` at module level into DFn(name, [], ..., expr), so
// a bare reference to `name` elsewhere must be lowered to `call @name()`
// — but only when `name` is actually such a declaration (NOT when it's a
// local let/lambda/pattern bind, which would shadow it).
let private collectNullaryGlobals (tm: TypedModule) : Set<string> =
    // Only collect zero-arg TDFn. TDLet is emitted as `@name = global ty v`
    // (see emitDecl), not as a callable function, so we must NOT rewrite
    // references to those into `call @name()` calls.
    tm.Decls
    |> List.choose (fun (decl, _) ->
        match decl with
        | TDFn(sig_, _, _) when List.isEmpty sig_.Params -> Some sig_.Name
        | _ -> None)
    |> Set.ofList

let private emitModule (tm: TypedModule) =
    resetCtorTags ()
    resetLambdas ()
    let (stringPool, stringDecls) = buildStringPool tm
    let nullaryGlobals = collectNullaryGlobals tm
    let declBodies =
        tm.Decls
        |> List.map fst
        |> List.map (emitDecl stringPool nullaryGlobals)
        |> List.filter (fun (s, _) -> not (String.IsNullOrWhiteSpace s))
    let needsRuntime = declBodies |> List.exists snd
    let body =
        declBodies
        |> List.map fst
        |> String.concat "\n\n"
    let stringBlock =
        if List.isEmpty stringDecls then ""
        else String.concat "\n" stringDecls + "\n\n"
    let runtime =
        if needsRuntime then emitRuntimeBlock () + "\n"
        else ""
    // Lifted lambdas are defined after the user decls — they may reference
    // globals declared above, and callers above them reach them by symbol.
    let lambdaBlock =
        if liftedLambdas.Count = 0 then ""
        else "\n\n" + String.concat "\n\n" (Seq.toList liftedLambdas)
    "; Generated by lllc (ll-lang LLVM backend)\n" +
    "; Module: " + moduleName tm.Path + "\n\n" +
    stringBlock + runtime + body + lambdaBlock + "\n"

let emit (tm: TypedModule) : string =
    resetLambdaCounter ()
    emitModule tm

let private isMainFn (sig_: TypedFnSig) =
    sig_.Name = "main" && List.isEmpty sig_.Params

let private moduleSuffix (tm: TypedModule) =
    let raw = String.concat "_" tm.Path
    if String.IsNullOrWhiteSpace raw then "Main" else raw.Replace(".", "_")

let private rewriteNonEntryMain (suffix: string) (tm: TypedModule) : TypedModule =
    let renamedDecls =
        tm.Decls
        |> List.map (fun (decl, exported) ->
            match decl with
            | TDFn(sig_, sch, body) when isMainFn sig_ ->
                let sig2 = { sig_ with Name = "__ll_main_" + suffix }
                (TDFn(sig2, sch, body), exported)
            | _ -> (decl, exported))
    { tm with Decls = renamedDecls }

let private declName (decl: TypedDecl) : string option =
    match decl with
    | TDFn(sig_, _, _) -> Some sig_.Name
    | TDExternal(sig_, _) -> Some sig_.Name
    | TDLet(name, _, _) -> Some name
    | _ -> None

let private dedupeModuleDecls (seen: Set<string>) (tm: TypedModule) : Set<string> * TypedModule =
    let (seen', kept) =
        tm.Decls
        |> List.fold (fun (accSeen, accDecls) (decl, exported) ->
            match declName decl with
            | None -> (accSeen, (decl, exported) :: accDecls)
            | Some name when Set.contains name accSeen ->
                // Keep the first declaration and drop later collisions so
                // the combined LLVM module remains linkable.
                (accSeen, accDecls)
            | Some name ->
                (Set.add name accSeen, (decl, exported) :: accDecls)
        ) (seen, [])
    (seen', { tm with Decls = List.rev kept })

let private defineRegex = Regex(@"^define\s+\S+\s+@([A-Za-z0-9_\.]+)\(", RegexOptions.Compiled)
let private declareRegex = Regex(@"^declare\s+\S+\s+@([A-Za-z0-9_\.]+)\(", RegexOptions.Compiled)
let private callRegex = Regex(@"call\s+(\S+)\s+@([A-Za-z0-9_\.]+)\(([^)]*)\)", RegexOptions.Compiled)

let private parseArgTypes (args: string) : string =
    let trimmed = args.Trim()
    if String.IsNullOrWhiteSpace trimmed then ""
    else
        trimmed.Split(',')
        |> Array.map (fun part ->
            let p = part.Trim()
            if String.IsNullOrWhiteSpace p then ""
            else
                let pieces = p.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                if pieces.Length = 0 then "" else pieces.[0])
        |> Array.filter (fun x -> x <> "")
        |> String.concat ", "

// Canonical runtime-primitive signatures. When the emitted .ll uses one of
// these names, prependExternalDecls forces the declare to match the runtime's
// C ABI, ignoring what the call-site types look like. This is required for
// higher-order primitives whose call sites vary by lambda signature (e.g.
// listFold is called as `i1 @listFold(ptr, i1, ptr)` in one place and
// `ptr @listFold(ptr, ptr, ptr)` in another — the linker sees only ONE
// C symbol, so the declare must match that C function's actual signature
// or else clang rejects the redefinition/LTO breaks at runtime ABI).
let private runtimeExternalSigs : Map<string, string * string> =
    Map.ofList [
        "listFold",   ("i64", "ptr, i64, ptr")
        "listFilter", ("ptr", "ptr, ptr")
        "listMap",    ("ptr", "ptr, ptr")
        "listHead",   ("ptr", "ptr")
        "listTail",   ("ptr", "ptr")
        "listReverse",("ptr", "ptr")
        "listAppend", ("ptr", "ptr, ptr")
        "listConcat", ("ptr", "ptr")
        "listLen",    ("i64", "ptr")
        "listIsEmpty",("i1",  "ptr")
    ]

let private prependExternalDecls (llvmText: string) : string =
    let lines = llvmText.Split('\n')
    let known =
        lines
        |> Array.choose (fun line ->
            let trimmed = line.Trim()
            let m1 = defineRegex.Match(trimmed)
            if m1.Success then Some m1.Groups.[1].Value
            else
                let m2 = declareRegex.Match(trimmed)
                if m2.Success then Some m2.Groups.[1].Value else None)
        |> Set.ofArray

    let calls =
        lines
        |> Array.choose (fun line ->
            let m = callRegex.Match(line)
            if m.Success then
                let retTy = m.Groups.[1].Value
                let name = m.Groups.[2].Value
                let argTys = parseArgTypes m.Groups.[3].Value
                Some (name, retTy, argTys)
            else None)
        |> Array.fold (fun acc (name, retTy, argTys) ->
            if Set.contains name known then acc
            elif Map.containsKey name acc then acc
            else Map.add name (retTy, argTys) acc
        ) Map.empty

    if Map.isEmpty calls then llvmText
    else
        let decls =
            calls
            |> Map.toList
            |> List.map (fun (name, (retTy, argTys)) ->
                // Override with canonical ABI if this name is a runtime primitive.
                match Map.tryFind name runtimeExternalSigs with
                | Some (r, a) -> "declare " + r + " @" + name + "(" + a + ")"
                | None -> "declare " + retTy + " @" + name + "(" + argTys + ")")
            |> String.concat "\n"
        decls + "\n\n" + llvmText

let emitProjectModules (tms: TypedModule list) : string =
    resetLambdaCounter ()
    match tms with
    | [] -> ""
    | [_] -> tms |> List.map emitModule |> String.concat "\n"
    | _ ->
        let lastIdx = List.length tms - 1
        let rewritten =
            tms
            |> List.mapi (fun i tm ->
                if i = lastIdx then tm
                else rewriteNonEntryMain (moduleSuffix tm) tm)
        let (_, deduped) =
            rewritten
            |> List.fold (fun (seen, acc) tm ->
                let (seen2, tm2) = dedupeModuleDecls seen tm
                (seen2, tm2 :: acc)
            ) (Set.empty, [])
        deduped
        |> List.rev
        |> List.map emitModule
        |> String.concat "\n"
        |> prependExternalDecls
