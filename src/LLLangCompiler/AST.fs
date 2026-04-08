module LLLang.AST

type Ident = string
type TypeIdent = string

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

// ---- Expressions ----------------------------------------------------

type Expr =
    | ELit of Literal
    | EVar of Ident
    | ECon of TypeIdent
    | EApp of Expr * Expr                     // f x (left-assoc juxtaposition)
    | ELam of Ident list * Expr               // \x y. e
    | ELet of Ident * Expr * Expr option      // let x = e (in e)?
    | EIf of Expr * Expr * Expr              // if e then e else e
    | EMatch of (Pattern * Expr) list         // | p -> e branches
    | EPipe of Expr * Expr                    // e -> e
    | ETagged of Expr * TypeIdent             // e[Tag]
    | EList of Expr list                      // [e e e]
    | ETuple of Expr list                     // e, e, e (2+ elements)

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
