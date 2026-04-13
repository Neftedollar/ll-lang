module LLLang.AST

type Ident = string
type TypeIdent = string

// ---- Source positions ------------------------------------------------
//
// The AST itself does NOT carry line / column fields on every node — that
// would be a lot of churn for marginal value. Instead we keep a side-table
// ("PosMap") keyed by boxed AST node reference and populated by the parser
// at construction time. Consumers (elaborator, HMInfer) look up positions
// when emitting errors, falling back to 0:0 if a node is absent (e.g.
// synthesized nodes that have no source location).

type Pos = { Line: int; Col: int }

/// A reference-keyed dictionary from AST nodes to their source position.
/// Uses physical (reference) equality because F# discriminated unions are
/// reference types but compare structurally by default — structural keys
/// would collapse duplicate sub-expressions into a single entry.
type PosMap =
    { Map: System.Collections.Generic.Dictionary<obj, Pos> }

module PosMap =
    let create () : PosMap =
        // Use .NET's built-in reference-equality comparer so we don't have
        // to hand-write one (and deal with nullable-object annotations).
        let cmp = System.Collections.Generic.ReferenceEqualityComparer.Instance
        { Map = System.Collections.Generic.Dictionary<obj, Pos>(cmp) }

    let empty () : PosMap = create ()

    /// Record the position of a single node. Idempotent (later writes win).
    /// `node` is typed `obj | null` only to silence the F# nullness analysis
    /// at call sites where `box x` is the input — we null-guard here.
    let add (pm: PosMap) (node: obj | null) (pos: Pos) : unit =
        match node with
        | null -> ()
        | n -> pm.Map[n] <- pos

    /// Look up a node's position, or return 0:0 if absent.
    let tryFind (pm: PosMap) (node: obj | null) : Pos =
        match node with
        | null -> { Line = 0; Col = 0 }
        | n ->
            match pm.Map.TryGetValue n with
            | true, p -> p
            | false, _ -> { Line = 0; Col = 0 }

// ---- Types ----------------------------------------------------------

type UnitExpr =
    | UName of TypeIdent                      // m, s, kg
    | UMul of UnitExpr * UnitExpr             // m*s
    | UDiv of UnitExpr * UnitExpr             // m/s
    | UPow of UnitExpr * int                  // m^2

type TypeExpr =
    | TyName of TypeIdent                     // Int, Str, Maybe
    | TyVar of Ident                          // a, b (type variables)
    | TyApp of TypeExpr * TypeExpr            // Maybe[Int]
    | TyFn of TypeExpr * TypeExpr             // A -> B
    | TyTagged of TypeExpr * UnitExpr         // Float[m/s]

/// Render a TypeExpr in source-syntax form for user-facing error messages.
/// Produces `Int`, `Maybe[Int]`, `A -> B`, `Float[m/s]`, `'$0` — never F# DU syntax.
let rec typeExprToStr (t: TypeExpr) : string =
    match t with
    | TyName n  -> n
    | TyVar v when v.Length > 0 && v.[0] = '$' -> $"'%s{v[1..]}"   // flex: '$0' → ''0'
    | TyVar v   -> $"'{v}"                                            // rigid: 'A
    | TyApp(TyName f, arg) -> $"%s{f}[%s{typeExprToStr arg}]"
    | TyApp(a, b) -> $"(%s{typeExprToStr a})[%s{typeExprToStr b}]"
    | TyFn(a, b)  -> $"%s{typeExprToStr a} -> %s{typeExprToStr b}"
    | TyTagged(a, u) -> $"%s{typeExprToStr a}[%s{unitExprToStr u}]"

and private unitExprToStr (u: UnitExpr) : string =
    match u with
    | UName n      -> n
    | UMul(a, b)   -> $"%s{unitExprToStr a}*%s{unitExprToStr b}"
    | UDiv(a, b)   -> $"%s{unitExprToStr a}/%s{unitExprToStr b}"
    | UPow(a, n)   -> $"%s{unitExprToStr a}^%d{n}"

// ---- Literals -------------------------------------------------------

type Literal =
    | LInt of int64
    | LFloat of float
    | LStr of string
    | LBool of bool
    | LChar of char

// ---- Patterns -------------------------------------------------------

type Pattern =
    | PVar of Ident                           // variable binding
    | PCon of TypeIdent * Pattern list        // Constructor p1 p2
    | PLit of Literal                         // 42, "hello", true
    | PWild                                   // _
    | PTuple of Pattern list                  // (p1, p2, ..., pN)
    | PCons of Pattern * Pattern              // head :: tail (list cons)

// ---- Expressions ----------------------------------------------------

type Expr =
    | ELit of Literal
    | EVar of Ident
    | ECon of TypeIdent
    | EApp of Expr * Expr                     // f x (left-assoc juxtaposition)
    | ELam of Ident list * Expr               // \x y. e
    | ELet of Ident * Expr * Expr option      // let x = e (in e)?
    | ELetPat of Pattern * Expr * Expr option // let (a, b) = e (in e)?  / let _ = e ...
    | EIf of Expr * Expr * Expr              // if e then e else e
    | EMatch of (Pattern * Expr) list         // | p -> e (implicit fn-body scrutinee)
    | EMatchOf of Expr * (Pattern * Expr) list // match scrut with | p -> e
    | EPipe of Expr * Expr                    // e -> e
    | ETagged of Expr * TypeIdent             // e[Tag]
    | EList of Expr list                      // [e e e]
    | ETuple of Expr list                     // e, e, e (2+ elements)
    | ECons of Expr * Expr                    // head :: tail (list cons)

// ---- Declarations ---------------------------------------------------

/// A typed parameter: (x Int)
type Param = Ident * TypeExpr

/// A type-class constraint on a fn: [F: Functor]
type Constraint = Ident * TypeIdent

type FnSig = {
    Name: Ident
    Constraints: Constraint list
    Params: Param list
    ReturnType: TypeExpr option
}

/// Type parameter: bare `A` or phantom `[state]`
type TypeParam =
    | TPBare of Ident
    | TPPhantom of Ident

type TypeBody =
    | TBSum of (TypeIdent * TypeExpr list) list     // Circle Float | Rect Float Float
    | TBRecord of (Ident * TypeExpr) list           // x Float, y Float
    | TBWrapped of TypeExpr                         // Int  (newtype-style)

type Decl =
    | DFn of FnSig * Expr
    | DLet of Ident * Expr
    | DLetPat of Pattern * Expr               // let (a, b) = e  /  let _ = e
    | DExternal of FnSig                      // external foo(x Int) Int
    | DOpaque of TypeIdent * TypeParam list   // opaque Promise[A]
    | DType of TypeIdent * TypeParam list * TypeBody
    | DTag of TypeIdent
    | DUnit of TypeIdent
    | DTrait of TypeIdent * Ident list * FnSig list
    | DImpl of TypeIdent * TypeIdent * (FnSig * Expr) list

// ---- Module ---------------------------------------------------------

type LLModule = {
    Path: string list                          // ["Examples"; "Basics"]
    Imports: string list list                  // [["Std"; "List"]; ...]
    Decls: (Decl * bool) list                  // (decl, isExported)
}
