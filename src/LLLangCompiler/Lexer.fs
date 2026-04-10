module LLLang.Lexer

open System
open LLLang.Token

let private keywords =
    Map.ofList [
        "let", KwLet
        "tag", KwTag; "unit", KwUnit; "trait", KwTrait; "impl", KwImpl
        "import", KwImport; "export", KwExport; "module", KwModule
        "if", KwIf; "else", KwElse
        "true", KwTrue; "false", KwFalse
        "match", KwMatch
    ]

let private isAlphaNum (c: char) = Char.IsLetterOrDigit c || c = '_'

/// Tokenize ll-lang source. Returns Tok list or error message.
/// INDENT/DEDENT synthetic tokens are injected based on indentation changes.
let tokenize (source: string) : Result<Tok list, string> =
    let result = ResizeArray<Tok>()
    let mutable pos = 0
    let mutable line = 1
    let mutable lineStart = 0
    let indentStack = Collections.Generic.Stack<int>([| 0 |])

    let cur () = if pos < source.Length then source[pos] else '\000'
    let peek () = if pos + 1 < source.Length then source[pos + 1] else '\000'
    let col () = pos - lineStart + 1
    let mk t = { Token = t; Line = line; Col = col() }
    let add t = result.Add(mk t)

    let advance () =
        if pos < source.Length then
            if source[pos] = '\n' then line <- line + 1; lineStart <- pos + 1
            pos <- pos + 1

    // Measure spaces at current position (without advancing pos permanently)
    // Returns indent level of the line starting at current pos.
    // Advances past the leading whitespace.
    let measureAndHandleIndent () =
        let mutable spaces = 0
        while pos < source.Length && (source[pos] = ' ' || source[pos] = '\t') do
            spaces <- spaces + (if source[pos] = '\t' then 4 else 1)
            pos <- pos + 1
        // Check if line is significant (not blank, not comment-only)
        let significant =
            pos < source.Length &&
            source[pos] <> '\n' && source[pos] <> '\r' &&
            not (source[pos] = '-' && pos + 1 < source.Length && source[pos + 1] = '-')
        if significant then
            let top = indentStack.Peek()
            if spaces > top then
                indentStack.Push(spaces)
                result.Add({ Token = Indent; Line = line; Col = 1 })
            elif spaces < top then
                while indentStack.Peek() > spaces do
                    indentStack.Pop() |> ignore
                    result.Add({ Token = Dedent; Line = line; Col = 1 })

    let rec scan () =
        if pos >= source.Length then ()
        else
        match cur() with
        | ' ' | '\t' -> advance(); scan()
        | '\r' -> advance(); scan()
        | '\n' ->
            let l, c = line, col()
            advance()
            result.Add({ Token = Newline; Line = l; Col = c })
            measureAndHandleIndent()
            scan()
        | '-' when peek() = '-' ->
            while pos < source.Length && source[pos] <> '\n' do advance()
            scan()
        | '-' when peek() = '>' -> let t = mk Arrow in advance(); advance(); result.Add(t); scan()
        | '<' when peek() = '=' -> let t = mk Le in advance(); advance(); result.Add(t); scan()
        | '>' when peek() = '=' -> let t = mk Ge in advance(); advance(); result.Add(t); scan()
        | '=' when peek() = '=' -> let t = mk EqEq in advance(); advance(); result.Add(t); scan()
        | '!' when peek() = '=' -> let t = mk Neq in advance(); advance(); result.Add(t); scan()
        | '-' -> add Minus; advance(); scan()
        | '+' -> add Plus; advance(); scan()
        | '*' -> add Star; advance(); scan()
        | '/' -> add Slash; advance(); scan()
        | '^' -> add Caret; advance(); scan()
        | '<' -> add Lt; advance(); scan()
        | '>' -> add Gt; advance(); scan()
        | '=' -> add Eq; advance(); scan()
        | ',' -> add Comma; advance(); scan()
        | '.' -> add Dot; advance(); scan()
        | ':' when peek() = ':' -> let t = mk ColonColon in advance(); advance(); result.Add(t); scan()
        | ':' -> add Colon; advance(); scan()
        | '|' -> add Bar; advance(); scan()
        | '[' -> add LBrack; advance(); scan()
        | ']' -> add RBrack; advance(); scan()
        | '(' -> add LParen; advance(); scan()
        | ')' -> add RParen; advance(); scan()
        | '\\' -> add Backslash; advance(); scan()
        | '_' when not (isAlphaNum (peek())) -> add Underscore; advance(); scan()
        | '"' ->
            let l, c = line, col()
            advance()
            let sb = Text.StringBuilder()
            let mutable closed = false
            while pos < source.Length && not closed do
                match source[pos] with
                | '"' -> advance(); closed <- true
                | '\\' ->
                    advance()
                    if pos < source.Length then
                        match source[pos] with
                        | 'n' -> sb.Append('\n') |> ignore; advance()
                        | 't' -> sb.Append('\t') |> ignore; advance()
                        | '"' -> sb.Append('"') |> ignore; advance()
                        | '\\' -> sb.Append('\\') |> ignore; advance()
                        | c2 -> sb.Append(c2) |> ignore; advance()
                | c2 -> sb.Append(c2) |> ignore; advance()
            result.Add({ Token = StrLit (sb.ToString()); Line = l; Col = c })
            if not closed then failwith $"Unterminated string literal at {l}:{c}"
            scan()
        | '\'' ->
            let l, c = line, col()
            advance()  // consume opening '
            if pos >= source.Length then
                failwith $"Unterminated char literal at {l}:{c}"
            let ch =
                if source[pos] = '\\' then
                    advance()
                    if pos >= source.Length then
                        failwith $"Unterminated char escape at {l}:{c}"
                    let esc =
                        match source[pos] with
                        | 'n' -> '\n'
                        | 't' -> '\t'
                        | 'r' -> '\r'
                        | '\\' -> '\\'
                        | '\'' -> '\''
                        | '"' -> '"'
                        | '0' -> '\000'
                        | other -> failwith $"Invalid char escape '\\{other}' at {l}:{c}"
                    advance()
                    esc
                else
                    let c2 = source[pos]
                    advance()
                    c2
            if pos >= source.Length || source[pos] <> '\'' then
                failwith $"Unterminated char literal at {l}:{c}"
            advance()  // consume closing '
            result.Add({ Token = CharLit ch; Line = l; Col = c })
            scan()
        | c when Char.IsDigit c ->
            let l, c2 = line, col()
            let start = pos
            while pos < source.Length && Char.IsDigit(source[pos]) do advance()
            if pos < source.Length && source[pos] = '.' && pos + 1 < source.Length && Char.IsDigit(source[pos + 1]) then
                advance()
                while pos < source.Length && Char.IsDigit(source[pos]) do advance()
                result.Add({ Token = FloatLit (Double.Parse(source[start..pos-1])); Line = l; Col = c2 })
            else
                result.Add { Token = IntLit (Int64.Parse(source[start..pos-1])); Line = l; Col = c2 }
            scan()
        | c when Char.IsLetter c || c = '_' ->
            let l, c2 = line, col()
            let start = pos
            while pos < source.Length && isAlphaNum source[pos] do advance()
            let s = source[start..pos-1]
            let tok =
                match Map.tryFind s keywords with
                | Some kw -> kw
                | None -> if Char.IsUpper source[start] then TypeId s else Ident s
            result.Add { Token = tok; Line = l; Col = c2 }
            scan()
        | _ -> advance(); scan()

    try
        scan()
        while indentStack.Count > 1 do
            indentStack.Pop() |> ignore
            result.Add { Token = Dedent; Line = line; Col = 1 }
        result.Add { Token = Eof; Line = line; Col = col() }
        Ok (List.ofSeq result)
    with ex ->
        Error ex.Message
