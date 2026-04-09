module LLLang.TypedAST

open LLLang.AST
open LLLang.Types

/// Unique ID per expression node (used for unique name generation in codegen)
type ExprId = int

/// Typed pattern (carries resolved type)
type TypedPattern = {
    Pat: Pattern
    Type: TypeExpr
}

/// Typed expression node
type TypedExpr = {
    Id: ExprId
    Type: TypeExpr
    Expr: TypedExprKind
}

and TypedExprKind =
    | TELit of Literal
    | TEVar of Ident
    | TECon of TypeIdent
    | TEApp of TypedExpr * TypedExpr
    | TELam of (Ident * TypeExpr) list * TypedExpr
    | TELet of Ident * TypeScheme * TypedExpr * TypedExpr option
    | TELetPat of TypedPattern * TypedExpr * TypedExpr option
    | TEIf of TypedExpr * TypedExpr * TypedExpr
    | TEMatch of TypedExpr * (TypedPattern * TypedExpr) list
    | TEMatchOf of TypedExpr * (TypedPattern * TypedExpr) list
    | TEPipe of TypedExpr * TypedExpr
    | TETagged of TypedExpr * TypeIdent
    | TEList of TypedExpr list
    | TETuple of TypedExpr list
    | TECons of TypedExpr * TypedExpr

/// Typed function signature
type TypedFnSig = {
    Name: Ident
    Constraints: Constraint list
    Params: (Ident * TypeExpr) list
    ReturnType: TypeExpr
}

/// Typed declaration
type TypedDecl =
    | TDFn of TypedFnSig * TypeScheme * TypedExpr
    | TDLet of Ident * TypeScheme * TypedExpr
    | TDLetPat of TypedPattern * TypedExpr
    | TDType of TypeIdent * TypeParam list * TypeBody
    | TDTag of TypeIdent
    | TDUnit of TypeIdent
    | TDTrait of TypeIdent * Ident list * FnSig list
    | TDImpl of TypeIdent * TypeIdent * (TypedFnSig * TypeScheme * TypedExpr) list

/// Complete typed module
type TypedModule = {
    Path: string list
    Decls: (TypedDecl * bool) list
    Env: Env
}
