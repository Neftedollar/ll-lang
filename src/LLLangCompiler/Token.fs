module LLLang.Token

/// All token types produced by the lexer.
type Token =
    // Keywords
    | KwFn | KwLet | KwType | KwTag | KwUnit
    | KwTrait | KwImpl | KwImport | KwExport | KwModule
    | KwIf | KwThen | KwElse | KwTrue | KwFalse
    | KwMatch | KwWith
    // Identifiers
    | Ident of string       // starts lowercase: variable, function name
    | TypeId of string      // starts uppercase: type, constructor, module segment
    // Literals
    | IntLit of int64
    | FloatLit of float
    | StrLit of string
    | CharLit of char
    // Operators
    | Arrow                 // ->
    | Backslash             // \
    | Dot                   // .
    | Comma                 // ,
    | Colon                 // :
    | ColonColon            // ::
    | Eq                    // =
    | Bar                   // |
    | LBrack                // [
    | RBrack                // ]
    | LParen                // (
    | RParen                // )
    | Plus | Minus | Star | Slash | Caret
    | Lt | Gt | Le | Ge | EqEq | Neq
    | Underscore            // _
    // Layout tokens (synthetic — injected by lexer based on indentation)
    | Indent | Dedent | Newline
    // End of input
    | Eof

/// A token with source position.
type Tok = { Token: Token; Line: int; Col: int }
