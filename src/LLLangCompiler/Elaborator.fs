module LLLang.Elaborator

open LLLang.AST

/// Symbol table: name → declared type.
type TypeEnv = Map<string, TypeExpr>

type ErrorCode = E001 | E002 | E003 | E004 | E005

type LLError = {
    Code: ErrorCode
    Line: int
    Col: int
    /// Compact format: "E001 12:5 TypeMismatch Str Str[UserId]"
    Message: string
}

let private e001 line col got expected = {
    Code = E001; Line = line; Col = col
    Message = sprintf "E001 %d:%d TypeMismatch %A %A" line col got expected }

let private e002 line col name = {
    Code = E002; Line = line; Col = col
    Message = sprintf "E002 %d:%d UnboundVar %s" line col name }

let private e003 line col typeName missing = {
    Code = E003; Line = line; Col = col
    Message = sprintf "E003 %d:%d NonExhaustiveMatch %s missing:%s" line col typeName missing }

let private e004 line col got expected = {
    Code = E004; Line = line; Col = col
    Message = sprintf "E004 %d:%d UnitMismatch %A %A" line col got expected }

let private e005 line col paramType argType = {
    Code = E005; Line = line; Col = col
    Message = sprintf "E005 %d:%d TagViolation %A %A" line col paramType argType }

/// Arithmetic and comparison operators pre-populated as TyVar wildcards.
/// These are parsed as EApp(EApp(EVar "+", ...), ...) and must not trigger E002
/// since they are never declared in source modules.
let private builtinEnv : TypeEnv =
    [ "+"; "-"; "*"; "/"; "=="; "!="; "<"; ">"; "<="; ">=" ]
    |> List.map (fun op -> op, TyFn(TyVar "a", TyFn(TyVar "a", TyVar "a")))
    |> Map.ofList

/// Elaborate an LLModule: build TypeEnv, check for errors.
/// Returns Ok TypeEnv on success, Error errors on any violation.
let elaborate (m: LLModule) : Result<TypeEnv, LLError list> =
    Ok Map.empty   // stub — replaced task by task
