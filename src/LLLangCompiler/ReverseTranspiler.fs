module LLLang.ReverseTranspiler

open System
open System.Globalization
open System.Text.RegularExpressions
open LLLang.Platform

type private ReverseDecl = {
    Index: int
    Name: string
    Value: string
}

type private ReverseFn = {
    Index: int
    Name: string
    Params: string list
    Body: string
}

type private ReverseTypeDecl = {
    Index: int
    Name: string
    Body: string
}

let private idRx = Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)
let private lllNameRx = Regex("^[a-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)

let private normalizeRecoveredName (raw: string) : string option =
    let name = raw.Trim()
    if String.IsNullOrWhiteSpace name then None
    elif lllNameRx.IsMatch(name) then Some name
    elif idRx.IsMatch(name) then
        let lowered =
            if Char.IsUpper(name.[0]) then
                string (Char.ToLowerInvariant(name.[0])) + name.Substring(1)
            else
                name
        if lllNameRx.IsMatch(lowered) then Some lowered else None
    else
        None

let private normalizeParamName (raw: string) : string option =
    let trimmed = raw.Trim().TrimStart('@')
    normalizeRecoveredName trimmed

let private tryMatchGroup (pattern: string) (src: string) (groupName: string) : string option =
    let m = Regex.Match(src, pattern, RegexOptions.Multiline)
    if m.Success then
        let v = m.Groups.[groupName].Value.Trim()
        if String.IsNullOrWhiteSpace v then None else Some v
    else
        None

let private fallbackModuleName = "Reverse.Generated"

let private inferModuleName (target: Target) (src: string) : string =
    match target with
    | FSharp ->
        tryMatchGroup @"^\s*module\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*$" src "name"
        |> Option.defaultValue fallbackModuleName
    | CSharp ->
        let cls =
            tryMatchGroup @"^\s*public\s+static\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*$" src "name"
            |> Option.defaultValue fallbackModuleName
        cls.Replace("_", ".")
    | LLVM ->
        tryMatchGroup @"^;\s*Module:\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*$" src "name"
        |> Option.defaultValue fallbackModuleName
    | TypeScript
    | Python
    | Java ->
        fallbackModuleName

let private dedupeDecls (decls: ReverseDecl list) : ReverseDecl list =
    let rec loop (seen: Set<string>) (acc: ReverseDecl list) (rest: ReverseDecl list) =
        match rest with
        | [] -> List.rev acc
        | d :: ds ->
            if Set.contains d.Name seen then
                loop seen acc ds
            else
                loop (Set.add d.Name seen) (d :: acc) ds
    loop Set.empty [] decls

let private dedupeFns (decls: ReverseFn list) : ReverseFn list =
    let rec loop (seen: Set<string>) (acc: ReverseFn list) (rest: ReverseFn list) =
        match rest with
        | [] -> List.rev acc
        | d :: ds ->
            if Set.contains d.Name seen then
                loop seen acc ds
            else
                loop (Set.add d.Name seen) (d :: acc) ds
    loop Set.empty [] decls

let private dedupeTypes (decls: ReverseTypeDecl list) : ReverseTypeDecl list =
    let rec loop (seen: Set<string>) (acc: ReverseTypeDecl list) (rest: ReverseTypeDecl list) =
        match rest with
        | [] -> List.rev acc
        | d :: ds ->
            if Set.contains d.Name seen then
                loop seen acc ds
            else
                loop (Set.add d.Name seen) (d :: acc) ds
    loop Set.empty [] decls

let private decodeStringEscapes (raw: string) : string =
    let sb = System.Text.StringBuilder(raw.Length)
    let mutable i = 0
    while i < raw.Length do
        let c = raw.[i]
        if c = '\\' && i + 1 < raw.Length then
            i <- i + 1
            let esc = raw.[i]
            match esc with
            | 'n' -> sb.Append('\n') |> ignore
            | 't' -> sb.Append('\t') |> ignore
            | 'r' -> sb.Append('\r') |> ignore
            | '\\' -> sb.Append('\\') |> ignore
            | '"' -> sb.Append('"') |> ignore
            | '\'' -> sb.Append('\'') |> ignore
            | '0' -> sb.Append('\000') |> ignore
            | '`' -> sb.Append('`') |> ignore
            | other -> sb.Append(other) |> ignore
        else
            sb.Append(c) |> ignore
        i <- i + 1
    sb.ToString()

let private encodeLllString (raw: string) : string =
    let sb = System.Text.StringBuilder(raw.Length + 8)
    sb.Append('"') |> ignore
    for c in raw do
        match c with
        | '\\' -> sb.Append("\\\\") |> ignore
        | '"' -> sb.Append("\\\"") |> ignore
        | '\n' -> sb.Append("\\n") |> ignore
        | '\t' -> sb.Append("\\t") |> ignore
        | '\r' -> sb.Append("\\r") |> ignore
        | '\000' -> sb.Append("\\0") |> ignore
        | other -> sb.Append(other) |> ignore
    sb.Append('"') |> ignore
    sb.ToString()

let private encodeLllChar (ch: char) : string =
    let body =
        match ch with
        | '\\' -> "\\\\"
        | '\'' -> "\\'"
        | '\n' -> "\\n"
        | '\t' -> "\\t"
        | '\r' -> "\\r"
        | '\000' -> "\\0"
        | c -> string c
    "'" + body + "'"

let private afterPreludeSection (src: string) : string =
    let markers = [ "// --- end prelude ---"; "# --- end prelude ---" ]
    let idx =
        markers
        |> List.map (fun m -> src.IndexOf(m, StringComparison.Ordinal))
        |> List.filter (fun i -> i >= 0)
        |> List.sortDescending
        |> List.tryHead
    match idx with
    | Some i -> src.Substring(i)
    | None -> src

let private normalizeRecoveredExpr (raw: string) : string option =
    let normalizeStrictEqualityOps (s: string) : string =
        let sb = System.Text.StringBuilder(s.Length)
        let mutable i = 0
        let mutable inString = false
        let mutable quote = '\000'
        let mutable escaped = false

        let startsWithAt (needle: string) (idx: int) =
            idx + needle.Length <= s.Length
            && String.Compare(s, idx, needle, 0, needle.Length, StringComparison.Ordinal) = 0

        while i < s.Length do
            let c = s.[i]
            if inString then
                sb.Append(c) |> ignore
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = quote then
                    inString <- false
            else
                if c = '"' || c = '\'' || c = '`' then
                    inString <- true
                    quote <- c
                    sb.Append(c) |> ignore
                elif startsWithAt "!==" i then
                    sb.Append("!=") |> ignore
                    i <- i + 2
                elif startsWithAt "===" i then
                    sb.Append("==") |> ignore
                    i <- i + 2
                else
                    sb.Append(c) |> ignore
            i <- i + 1
        sb.ToString()

    let normalizeSingleEqualsComparisons (s: string) : string =
        let sb = System.Text.StringBuilder(s.Length + 8)
        let mutable i = 0
        let mutable inString = false
        let mutable quote = '\000'
        let mutable escaped = false

        let prevNonWsIdx (idx: int) =
            let mutable j = idx - 1
            while j >= 0 && Char.IsWhiteSpace(s.[j]) do
                j <- j - 1
            j

        let nextNonWsIdx (idx: int) =
            let mutable j = idx + 1
            while j < s.Length && Char.IsWhiteSpace(s.[j]) do
                j <- j + 1
            j

        while i < s.Length do
            let c = s.[i]
            if inString then
                sb.Append(c) |> ignore
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = quote then
                    inString <- false
            else
                if c = '"' || c = '\'' || c = '`' then
                    inString <- true
                    quote <- c
                    sb.Append(c) |> ignore
                elif c = '=' then
                    let prevIdx = prevNonWsIdx i
                    let nextIdx = nextNonWsIdx i
                    let prevC = if prevIdx >= 0 then Some s.[prevIdx] else None
                    let nextC = if nextIdx < s.Length then Some s.[nextIdx] else None
                    let isComposite =
                        match prevC, nextC with
                        | Some ('<' | '>' | '!' | '='), _
                        | _, Some ('=' | '>') -> true
                        | _ -> false
                    if isComposite then
                        sb.Append('=') |> ignore
                    else
                        sb.Append("==") |> ignore
                else
                    sb.Append(c) |> ignore
            i <- i + 1
        sb.ToString()

    let normalizeFSharpIfConditionComparisons (s: string) : string =
        Regex.Replace(
            s,
            @"\bif\s+(?<cond>.+?)\s+then",
            MatchEvaluator(fun m ->
                let cond = normalizeSingleEqualsComparisons (m.Groups.["cond"].Value)
                "if " + cond + " then"),
            RegexOptions.Singleline)

    let findMatchingParenAt (s: string) (openIdx: int) : int option =
        if openIdx < 0 || openIdx >= s.Length || s.[openIdx] <> '(' then
            None
        else
            let mutable depth = 0
            let mutable inString = false
            let mutable quote = '\000'
            let mutable escaped = false
            let mutable found = -1
            let mutable i = openIdx
            while i < s.Length && found < 0 do
                let c = s.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = quote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        quote <- c
                    | '(' -> depth <- depth + 1
                    | ')' ->
                        depth <- depth - 1
                        if depth = 0 then
                            found <- i
                    | _ -> ()
                i <- i + 1
            if found >= 0 then Some found else None

    let rec stripHostCastWrappers (raw: string) : string =
        let t = raw.Trim()
        let tryStripCheckedUnchecked (input: string) : string option =
            let stripWrapper (name: string) =
                if input.StartsWith(name, StringComparison.Ordinal) then
                    let openIdx = name.Length
                    if openIdx < input.Length && input.[openIdx] = '(' then
                        match findMatchingParenAt input openIdx with
                        | Some closeIdx when closeIdx = input.Length - 1 ->
                            Some (input.Substring(openIdx + 1, closeIdx - openIdx - 1).Trim())
                        | _ -> None
                    else
                        None
                else
                    None
            match stripWrapper "unchecked" with
            | Some inner -> Some inner
            | None -> stripWrapper "checked"

        let tryStripPrimitiveCast (input: string) : string option =
            let m =
                Regex.Match(
                    input,
                    @"^\(\s*(?<ty>sbyte|byte|short|ushort|int|uint|long|ulong|float|double|decimal|bool|char|string|Int16|Int32|Int64|UInt16|UInt32|UInt64|Single|Double|Decimal|Boolean|Char|String)\s*\)\s*(?<rest>.+)$",
                    RegexOptions.CultureInvariant
                )
            if m.Success then
                Some (m.Groups.["rest"].Value.Trim())
            else
                None

        let t1 =
            match tryStripCheckedUnchecked t with
            | Some inner when not (String.IsNullOrWhiteSpace inner) -> inner
            | _ -> t
        let t2 =
            match tryStripPrimitiveCast t1 with
            | Some inner when not (String.IsNullOrWhiteSpace inner) -> inner
            | _ -> t1
        if StringComparer.Ordinal.Equals(t2, t) then
            t
        else
            stripHostCastWrappers t2

    let splitTopLevelCommaArgs (s: string) : string list =
        let parts = ResizeArray<string>()
        let mutable start = 0
        let mutable paren = 0
        let mutable bracket = 0
        let mutable brace = 0
        let mutable inString = false
        let mutable quote = '\000'
        let mutable escaped = false
        let mutable i = 0
        while i < s.Length do
            let c = s.[i]
            if inString then
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = quote then
                    inString <- false
            else
                match c with
                | '"' | '\'' | '`' ->
                    inString <- true
                    quote <- c
                | '(' -> paren <- paren + 1
                | ')' when paren > 0 -> paren <- paren - 1
                | '[' -> bracket <- bracket + 1
                | ']' when bracket > 0 -> bracket <- bracket - 1
                | '{' -> brace <- brace + 1
                | '}' when brace > 0 -> brace <- brace - 1
                | ',' when paren = 0 && bracket = 0 && brace = 0 ->
                    parts.Add(s.Substring(start, i - start).Trim())
                    start <- i + 1
                | _ -> ()
            i <- i + 1
        let tail = s.Substring(start).Trim()
        if parts.Count > 0 then
            if not (String.IsNullOrWhiteSpace tail) then parts.Add(tail)
            parts |> Seq.toList
        else
            []

    let normalizeTupleCtorCalls (s: string) : string =
        let sb = System.Text.StringBuilder(s.Length + 8)
        let mutable i = 0
        let mutable inString = false
        let mutable quote = '\000'
        let mutable escaped = false
        while i < s.Length do
            let c = s.[i]
            if inString then
                sb.Append(c) |> ignore
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = quote then
                    inString <- false
                i <- i + 1
            elif c = '"' || c = '\'' || c = '`' then
                inString <- true
                quote <- c
                sb.Append(c) |> ignore
                i <- i + 1
            elif Char.IsUpper c then
                let mutable j = i + 1
                while j < s.Length && (Char.IsLetterOrDigit s.[j] || s.[j] = '_') do
                    j <- j + 1
                let name = s.Substring(i, j - i)
                let mutable k = j
                while k < s.Length && Char.IsWhiteSpace s.[k] do
                    k <- k + 1
                if k < s.Length && s.[k] = '(' then
                    match findMatchingParenAt s k with
                    | Some closeIdx ->
                        let inner = s.Substring(k + 1, closeIdx - k - 1)
                        let args = splitTopLevelCommaArgs inner
                        if args.Length >= 2 then
                            sb.Append(name) |> ignore
                            for arg in args do
                                sb.Append(' ') |> ignore
                                sb.Append(arg) |> ignore
                            i <- closeIdx + 1
                        else
                            sb.Append(name) |> ignore
                            i <- j
                    | None ->
                        sb.Append(name) |> ignore
                        i <- j
                else
                    sb.Append(name) |> ignore
                    i <- j
            else
                sb.Append(c) |> ignore
                i <- i + 1
        sb.ToString()

    let normalizeSimpleTupleCtorCalls (s: string) : string =
        let rx =
            Regex(
                @"\b(?<ctor>[A-Z][A-Za-z0-9_]*)\s+\((?<args>[^()\r\n]+)\)",
                RegexOptions.Compiled
            )

        let rewrite (input: string) =
            rx.Replace(
                input,
                MatchEvaluator(fun m ->
                    let ctor = m.Groups.["ctor"].Value
                    let argsRaw = m.Groups.["args"].Value
                    let args =
                        argsRaw.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map (fun a -> a.Trim())
                        |> Array.filter (fun a -> not (String.IsNullOrWhiteSpace a))
                        |> Array.toList
                    if args.Length >= 2 then
                        ctor + " " + String.concat " " args
                    else
                        m.Value))

        let mutable prev = s
        let mutable curr = rewrite prev
        let mutable guard = 0
        while curr <> prev && guard < 8 do
            prev <- curr
            curr <- rewrite prev
            guard <- guard + 1
        curr

    let indentBlock (text: string) : string =
        text.Replace("\r\n", "\n")
            .Split('\n')
        |> Array.map (fun line -> "  " + line)
        |> String.concat "\n"

    let renderIfExpr (cond: string) (thenBody: string) (elseBody: string) : string =
        let thenIndented = indentBlock (thenBody.Trim())
        let elseTrimmed = elseBody.Trim()
        if elseTrimmed.Contains("\n", StringComparison.Ordinal) then
            if elseTrimmed.StartsWith("if ", StringComparison.Ordinal) then
                "if " + cond + "\n" + thenIndented + "\nelse " + elseTrimmed
            else
                "if " + cond + "\n" + thenIndented + "\nelse\n" + (indentBlock elseTrimmed)
        else
            "if " + cond + "\n" + thenIndented + "\nelse " + elseTrimmed

    let tryInlineFlattenedLetBindings (flattened: string) : string option =
        let lines =
            flattened.Replace("\r\n", "\n").Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
            |> Array.toList

        let bindRx = Regex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<rhs>.+)$", RegexOptions.Compiled)

        let rec splitBindings (acc: (string * string) list) (rest: string list) =
            match rest with
            | [] -> List.rev acc, []
            | line :: tail ->
                let m = bindRx.Match(line)
                if m.Success then
                    let name = m.Groups.["name"].Value
                    let rhs = m.Groups.["rhs"].Value.Trim()
                    splitBindings ((name, rhs) :: acc) tail
                else
                    List.rev acc, rest

        let bindings, bodyLines = splitBindings [] lines
        // Be intentionally strict here: broad inlining of large let-chains
        // can drop sequencing/side-effects and corrupt recovered control flow.
        if bindings.Length <> 1 || List.isEmpty bodyLines then
            None
        else
            let (name, rhs) = List.head bindings
            if name = "_" || rhs.Contains("\n", StringComparison.Ordinal) then
                None
            else
            let body = String.concat "\n" bodyLines
            let inlined =
                Regex.Replace(
                    body,
                    $@"\b{Regex.Escape(name)}\b",
                    "(" + rhs + ")",
                    RegexOptions.None
                )
                    .Trim()
            if String.IsNullOrWhiteSpace inlined then None else Some inlined

    let stripTrailingStandaloneExitCode (s: string) : string =
        let raw = s.Replace("\r\n", "\n")
        let lines = raw.Split('\n')
        let mutable last = lines.Length - 1
        while last >= 0 && String.IsNullOrWhiteSpace(lines.[last]) do
            last <- last - 1
        if last <= 0 then
            raw
        elif not (Regex.IsMatch(lines.[last].Trim(), @"^-?\d+L?$")) then
            raw
        else
            let mutable prev = last - 1
            while prev >= 0 && String.IsNullOrWhiteSpace(lines.[prev]) do
                prev <- prev - 1
            if prev >= 0 && lines.[prev].TrimEnd().EndsWith(")", StringComparison.Ordinal) then
                String.Join("\n", lines.[0..last - 1]).TrimEnd()
            else
                raw

    let collapseLeadingDuplicateNumericLine (s: string) : string =
        let raw = s.Replace("\r\n", "\n")
        let lines = raw.Split('\n')
        let nonEmpty =
            lines
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
        let isNumericLine (line: string) =
            Regex.IsMatch(line, @"^-?\d+L?$")
        if nonEmpty.Length = 2 && isNumericLine nonEmpty.[0] && isNumericLine nonEmpty.[1] then
            nonEmpty.[1]
        else
            raw

    let normalizeSimpleTemplateLiterals (s: string) : string =
        // Convert plain template literals (without interpolation) into
        // ll-lang string literals so the parser can round-trip TS output.
        Regex.Replace(
            s,
            @"`(?<body>(?:\\.|[^`])*)`",
            MatchEvaluator(fun (m: Match) ->
                let body = m.Groups.["body"].Value
                if body.Contains("${", StringComparison.Ordinal) then
                    m.Value
                else
                    body |> decodeStringEscapes |> encodeLllString
            )
        )

    let normalizeHostConsoleWrappers (s: string) : string =
        let toPrint pattern (input: string) =
            Regex.Replace(
                input,
                pattern,
                MatchEvaluator(fun (m: Match) ->
                    let arg = m.Groups.["arg"].Value.Trim()
                    if String.IsNullOrWhiteSpace arg then
                        m.Value
                    else
                        "print(" + arg + ")"
                )
            )

        s
        |> toPrint @"\bSystem\.Console\.WriteLine\(\s*(?<arg>[^()]*)\s*\)"
        |> toPrint @"\bConsole\.WriteLine\(\s*(?<arg>[^()]*)\s*\)"
        |> toPrint @"\bconsole\.log\(\s*(?<arg>[^()]*)\s*\)"
        |> toPrint @"\bSystem\.out\.println\(\s*(?<arg>[^()]*)\s*\)"

    let normalizeHostMaybeConstructors (s: string) : string =
        let normalizeSome pattern (input: string) =
            Regex.Replace(
                input,
                pattern,
                MatchEvaluator(fun (m: Match) ->
                    let arg = m.Groups.["arg"].Value.Trim()
                    if String.IsNullOrWhiteSpace arg then
                        m.Value
                    else
                        "Some (" + arg + ")"
                )
            )

        s
        |> fun x -> Regex.Replace(x, @"\bnew\s+(?:Maybe\.)?None(?:<[^>]+>)?\s*\(\s*\)", "None")
        |> normalizeSome @"\bnew\s+(?:Maybe\.)?Some(?:<[^>]+>)?\s*\(\s*\((?<arg>[^)]*)\)\s*\)"
        |> normalizeSome @"\bnew\s+(?:Maybe\.)?Some(?:<[^>]+>)?\s*\(\s*(?<arg>[^)]*)\s*\)"

    let normalizeHostMaybeMatchWrappers (s: string) : string =
        let tryNormalizeCsharpLiftedMaybeMatch (input: string) : string option =
            let t = input.Trim()
            if not (t.Contains(" is Some<", StringComparison.Ordinal)
                    && t.Contains(" is None<", StringComparison.Ordinal)
                    && t.Contains("._0", StringComparison.Ordinal)) then
                None
            else
                let scrutM = Regex.Match(t, @"var\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?<scrut>[A-Za-z_][A-Za-z0-9_]*)\s*;")
                let bindM = Regex.Match(t, @"var\s+(?<bind>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[A-Za-z_][A-Za-z0-9_]*\._0\s*;")
                let noneM =
                    Regex.Match(
                        t,
                        @"is\s+None<[^>]+>\)\s*\?\s*\(\(Func<[^>]+>\)\(\(\)\s*=>\s*\{[\s\S]*?return\s+(?<none>-?\d+L?)\s*;",
                        RegexOptions.Singleline)
                if scrutM.Success && bindM.Success && noneM.Success then
                    let scrut = scrutM.Groups.["scrut"].Value
                    let bind = bindM.Groups.["bind"].Value
                    let noneRaw = noneM.Groups.["none"].Value
                    let noneExpr = Regex.Replace(noneRaw, @"\b(-?\d+)L\b", "$1")
                    if String.IsNullOrWhiteSpace scrut
                       || String.IsNullOrWhiteSpace bind
                       || String.IsNullOrWhiteSpace noneExpr then
                        None
                    else
                        Some (sprintf "match %s | Some(%s) -> %s | None -> %s" scrut bind bind noneExpr)
                else
                    None

        let tryExtractSomeCondVar (condRaw: string) : string option =
            let cond = condRaw.Trim()
            let tryVar pattern =
                let m = Regex.Match(cond, pattern, RegexOptions.CultureInvariant)
                if m.Success then Some m.Groups.["var"].Value else None

            tryVar @"^\(?\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\??\._tag\s*==\s*[`""]Some[`""]\s*\)?$"
            |> Option.orElseWith (fun () -> tryVar @"^\(?\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s+instanceof\s+(?:Maybe\.)?Some(?:<[^>]+>)?\s*\)?$")
            |> Option.orElseWith (fun () -> tryVar @"^\(?\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\s+is\s+Some(?:<[^>]+>)?\s*\)?$")

        let tryExtractSomePayloadBinder (scrut: string) (thenRaw: string) : string option =
            let thenExpr = thenRaw.Trim()
            let bindIfVarEq (v: string) =
                if String.Equals(v, scrut, StringComparison.Ordinal) then
                    Some "n"
                else
                    None

            let mDirect = Regex.Match(thenExpr, @"^\(?\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\._0(?:\(\))?\s*\)?$")
            if mDirect.Success then
                bindIfVarEq mDirect.Groups.["var"].Value
            else
                let mCast = Regex.Match(thenExpr, @"^\(?\s*\(\([^)]+\)\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\)\._0(?:\(\))?\s*\)?$")
                if mCast.Success then
                    bindIfVarEq mCast.Groups.["var"].Value
                else
                    let mLambda =
                        Regex.Match(
                            thenExpr,
                            @"^\(lambda\s+(?<n>[A-Za-z_][A-Za-z0-9_]*):\s*(?<ret>[A-Za-z_][A-Za-z0-9_]*)\)\(\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\._0\s*\)$")
                    if mLambda.Success
                        && String.Equals(mLambda.Groups.["n"].Value, mLambda.Groups.["ret"].Value, StringComparison.Ordinal)
                        && String.Equals(mLambda.Groups.["var"].Value, scrut, StringComparison.Ordinal) then
                        Some mLambda.Groups.["n"].Value
                    else
                        None

        let tryBuildMaybeMatch (condRaw: string) (thenRaw: string) (elseRaw: string) : string option =
            match tryExtractSomeCondVar condRaw with
            | None -> None
            | Some scrut ->
                match tryExtractSomePayloadBinder scrut thenRaw with
                | None -> None
                | Some bind ->
                    let elseExpr = elseRaw.Trim()
                    if String.IsNullOrWhiteSpace elseExpr then
                        None
                    else
                        Some (sprintf "match %s | Some(%s) -> %s | None -> %s" scrut bind bind elseExpr)

        let tryNormalizeMultilineIf (input: string) : string option =
            let m =
                Regex.Match(
                    input.Trim(),
                    @"(?ms)^\s*if\s+(?<cond>[^\r\n]+)\s*\r?\n\s*(?<then>.+?)\s*\r?\n\s*else\s+(?<else>.+)\s*$",
                    RegexOptions.CultureInvariant)
            if m.Success then
                tryBuildMaybeMatch m.Groups.["cond"].Value m.Groups.["then"].Value m.Groups.["else"].Value
            else
                None

        let tryNormalizeTernary (input: string) : string option =
            let m =
                Regex.Match(
                    input.Trim(),
                    @"^\(?\s*(?<cond>.+?)\s*\?\s*(?<then>.+?)\s*:\s*(?<else>.+?)\s*\)?$",
                    RegexOptions.Singleline ||| RegexOptions.CultureInvariant)
            if m.Success then
                tryBuildMaybeMatch m.Groups.["cond"].Value m.Groups.["then"].Value m.Groups.["else"].Value
            else
                None

        let tryNormalizePythonTernary (input: string) : string option =
            let m =
                Regex.Match(
                    input.Trim(),
                    @"^\(?\s*(?<then>.+?)\s+if\s+(?<cond>.+?)\s+else\s+(?<else>.+?)\s*\)?$",
                    RegexOptions.Singleline ||| RegexOptions.CultureInvariant)
            if m.Success then
                tryBuildMaybeMatch m.Groups.["cond"].Value m.Groups.["then"].Value m.Groups.["else"].Value
            else
                None

        match tryNormalizeCsharpLiftedMaybeMatch s with
        | Some normalized -> normalized
        | None ->
            match tryNormalizeMultilineIf s with
            | Some normalized -> normalized
            | None ->
                match tryNormalizeTernary s with
                | Some normalized -> normalized
                | None ->
                    match tryNormalizePythonTernary s with
                    | Some normalized -> normalized
                    | None -> s

    let normalizePythonIntDiv (s: string) : string =
        let sb = System.Text.StringBuilder(s.Length)
        let mutable i = 0
        let mutable inString = false
        let mutable quote = '\000'
        let mutable escaped = false
        while i < s.Length do
            let c = s.[i]
            if inString then
                sb.Append(c) |> ignore
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = quote then
                    inString <- false
                i <- i + 1
            else
                if c = '"' || c = '\'' || c = '`' then
                    inString <- true
                    quote <- c
                    sb.Append(c) |> ignore
                    i <- i + 1
                elif i + 1 < s.Length && s.[i] = '/' && s.[i + 1] = '/' then
                    sb.Append('/') |> ignore
                    i <- i + 2
                else
                    sb.Append(c) |> ignore
                    i <- i + 1
        sb.ToString()

    let hasBalancedOuterParens (s: string) : bool =
        if String.IsNullOrWhiteSpace s || not (s.StartsWith("(") && s.EndsWith(")")) then
            false
        else
            let mutable paren = 0
            let mutable bracket = 0
            let mutable brace = 0
            let mutable inString = false
            let mutable stringQuote = '\000'
            let mutable escaped = false
            let mutable valid = true
            let mutable i = 0
            while i < s.Length && valid do
                let c = s.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = stringQuote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        stringQuote <- c
                    | '(' -> paren <- paren + 1
                    | ')' ->
                        if paren <= 0 then
                            valid <- false
                        else
                            paren <- paren - 1
                            if paren = 0 && i < s.Length - 1 then
                                valid <- false
                    | '[' -> bracket <- bracket + 1
                    | ']' when bracket > 0 -> bracket <- bracket - 1
                    | '{' -> brace <- brace + 1
                    | '}' when brace > 0 -> brace <- brace - 1
                    | _ -> ()
                i <- i + 1
            valid && paren = 0 && bracket = 0 && brace = 0 && not inString

    let trimParens (s: string) =
        let rec strip (t: string) =
            let t1 = t.Trim()
            if t1.Length >= 2 && hasBalancedOuterParens t1 then
                strip (t1.Substring(1, t1.Length - 2))
            else
                t1
        strip s

    let trySplitTopLevelTernary (s: string) : (string * string * string) option =
        let mutable paren = 0
        let mutable bracket = 0
        let mutable brace = 0
        let mutable inString = false
        let mutable stringQuote = '\000'
        let mutable escaped = false
        let mutable qIdx = -1
        let mutable colonIdx = -1
        let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString

        let mutable i = 0
        while i < s.Length && (qIdx < 0 || colonIdx < 0) do
            let c = s.[i]
            if inString then
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = stringQuote then
                    inString <- false
            else
                match c with
                | '"' | '\'' | '`' ->
                    inString <- true
                    stringQuote <- c
                | '(' -> paren <- paren + 1
                | ')' when paren > 0 -> paren <- paren - 1
                | '[' -> bracket <- bracket + 1
                | ']' when bracket > 0 -> bracket <- bracket - 1
                | '{' -> brace <- brace + 1
                | '}' when brace > 0 -> brace <- brace - 1
                | '?' when isTopLevel () && qIdx < 0 ->
                    // Skip null-coalescing and null-propagation forms.
                    if i + 1 < s.Length then
                        let n = s.[i + 1]
                        if n = '?' || n = '.' then () else qIdx <- i
                    else
                        qIdx <- i
                | ':' when isTopLevel () && qIdx >= 0 && colonIdx < 0 ->
                    colonIdx <- i
                | _ -> ()
            i <- i + 1

        if qIdx > 0 && colonIdx > qIdx + 1 then
            let cond = s.Substring(0, qIdx).Trim()
            let tBranch = s.Substring(qIdx + 1, colonIdx - qIdx - 1).Trim()
            let fBranch = s.Substring(colonIdx + 1).Trim()
            if String.IsNullOrWhiteSpace cond
               || String.IsNullOrWhiteSpace tBranch
               || String.IsNullOrWhiteSpace fBranch then
                None
            else
                Some (cond, tBranch, fBranch)
        else
            None

    let trySplitTopLevelPythonTernary (s: string) : (string * string * string) option =
        let mutable paren = 0
        let mutable bracket = 0
        let mutable brace = 0
        let mutable inString = false
        let mutable stringQuote = '\000'
        let mutable escaped = false
        let mutable ifIdx = -1
        let mutable elseIdx = -1
        let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString
        let isBoundary (i: int) =
            i < 0 || i >= s.Length || (not (Char.IsLetterOrDigit s.[i]) && s.[i] <> '_')
        let startsWithWord (i: int) (word: string) =
            i >= 0
            && i + word.Length <= s.Length
            && String.Compare(s, i, word, 0, word.Length, StringComparison.Ordinal) = 0
            && isBoundary (i - 1)
            && isBoundary (i + word.Length)

        let mutable i = 0
        while i < s.Length && (ifIdx < 0 || elseIdx < 0) do
            let c = s.[i]
            if inString then
                if escaped then
                    escaped <- false
                elif c = '\\' then
                    escaped <- true
                elif c = stringQuote then
                    inString <- false
            else
                match c with
                | '"' | '\'' | '`' ->
                    inString <- true
                    stringQuote <- c
                | '(' -> paren <- paren + 1
                | ')' when paren > 0 -> paren <- paren - 1
                | '[' -> bracket <- bracket + 1
                | ']' when bracket > 0 -> bracket <- bracket - 1
                | '{' -> brace <- brace + 1
                | '}' when brace > 0 -> brace <- brace - 1
                | _ ->
                    if isTopLevel () then
                        if ifIdx < 0 && startsWithWord i "if" then
                            ifIdx <- i
                            i <- i + 1
                        elif ifIdx >= 0 && elseIdx < 0 && startsWithWord i "else" then
                            elseIdx <- i
                            i <- i + 3
            i <- i + 1

        if ifIdx > 0 && elseIdx > ifIdx + 2 then
            let tBranch = s.Substring(0, ifIdx).Trim()
            let cond = s.Substring(ifIdx + 2, elseIdx - (ifIdx + 2)).Trim()
            let fBranch = s.Substring(elseIdx + 4).Trim()
            if String.IsNullOrWhiteSpace cond
               || String.IsNullOrWhiteSpace tBranch
               || String.IsNullOrWhiteSpace fBranch
               // Python ternary here is expected to be a single-line expression.
               // Multiline forms usually indicate control-flow `if` and must not
               // be rewritten as expression ternary.
               || cond.Contains("\n", StringComparison.Ordinal)
               || tBranch.Contains("\n", StringComparison.Ordinal)
               || fBranch.Contains("\n", StringComparison.Ordinal)
               || tBranch.Contains("\r", StringComparison.Ordinal)
               || cond.Contains("\r", StringComparison.Ordinal)
               || fBranch.Contains("\r", StringComparison.Ordinal) then
                None
            else
                Some (cond, tBranch, fBranch)
        else
            None

    let trySplitTopLevelFSharpIfThenElse (s: string) : (string * string * string) option =
        let t = s.Trim()
        if not (t.StartsWith("if ", StringComparison.Ordinal)) then
            None
        else
            let mutable paren = 0
            let mutable bracket = 0
            let mutable brace = 0
            let mutable inString = false
            let mutable stringQuote = '\000'
            let mutable escaped = false
            let mutable thenIdx = -1
            let mutable elseIdx = -1
            let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString
            let isBoundary (i: int) =
                i < 0 || i >= t.Length || (not (Char.IsLetterOrDigit t.[i]) && t.[i] <> '_')
            let startsWithWord (i: int) (word: string) =
                i >= 0
                && i + word.Length <= t.Length
                && String.Compare(t, i, word, 0, word.Length, StringComparison.Ordinal) = 0
                && isBoundary (i - 1)
                && isBoundary (i + word.Length)

            let mutable i = 0
            while i < t.Length && (thenIdx < 0 || elseIdx < 0) do
                let c = t.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = stringQuote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        stringQuote <- c
                    | '(' -> paren <- paren + 1
                    | ')' when paren > 0 -> paren <- paren - 1
                    | '[' -> bracket <- bracket + 1
                    | ']' when bracket > 0 -> bracket <- bracket - 1
                    | '{' -> brace <- brace + 1
                    | '}' when brace > 0 -> brace <- brace - 1
                    | _ ->
                        if isTopLevel () then
                            if thenIdx < 0 && startsWithWord i "then" then
                                thenIdx <- i
                                i <- i + 3
                            elif thenIdx >= 0 && elseIdx < 0 && startsWithWord i "else" then
                                elseIdx <- i
                                i <- i + 3
                i <- i + 1

            if thenIdx > 2 && elseIdx > thenIdx + 4 then
                let cond = t.Substring(2, thenIdx - 2).Trim()
                let tBranch = t.Substring(thenIdx + 4, elseIdx - (thenIdx + 4)).Trim()
                let fBranch = t.Substring(elseIdx + 4).Trim()
                if String.IsNullOrWhiteSpace cond
                   || String.IsNullOrWhiteSpace tBranch
                   || String.IsNullOrWhiteSpace fBranch then
                    None
                else
                    Some (cond, tBranch, fBranch)
            else
                None

    let findMatchingParen (s: string) (openIdx: int) : int option =
        if openIdx < 0 || openIdx >= s.Length || s.[openIdx] <> '(' then
            None
        else
            let mutable depth = 0
            let mutable inString = false
            let mutable quote = '\000'
            let mutable escaped = false
            let mutable result = -1
            let mutable i = openIdx
            while i < s.Length && result < 0 do
                let c = s.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = quote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        quote <- c
                    | '(' -> depth <- depth + 1
                    | ')' ->
                        depth <- depth - 1
                        if depth = 0 then
                            result <- i
                    | _ -> ()
                i <- i + 1
            if result >= 0 then Some result else None

    let rec normalizeCore (s: string) : string =
        let t = s.Trim()
        let t1 = trimParens t
        match tryFlattenFSharpLetInChain t1 with
        | Some flattened ->
            flattened
        | None ->
            match trySplitTopLevelTernary t1 with
            | Some (cond, tBranch, fBranch) ->
                let thenBody = normalizeCore tBranch
                let elseBody = normalizeCore fBranch
                renderIfExpr (normalizeCore cond) thenBody elseBody
            | None ->
                match trySplitTopLevelPythonTernary t1 with
                | Some (cond, tBranch, fBranch) ->
                    let thenBody = normalizeCore tBranch
                    let elseBody = normalizeCore fBranch
                    renderIfExpr (normalizeCore cond) thenBody elseBody
                | None ->
                    match trySplitTopLevelFSharpIfThenElse t1 with
                    | Some (cond, tBranch, fBranch) ->
                        let thenBody = normalizeCore tBranch
                        let elseBody = normalizeCore fBranch
                        let condNorm = normalizeCore (normalizeSingleEqualsComparisons cond)
                        renderIfExpr condNorm thenBody elseBody
                    | None ->
                        let nestedLets = normalizeNestedParenthesizedLetIns t1
                        if nestedLets <> t1 then
                            normalizeCore nestedLets
                        else
                            let nestedIfs = normalizeNestedParenthesizedFSharpIfs t1
                            if nestedIfs <> t1 then
                                normalizeCore nestedIfs
                            else
                                t1
                                |> normalizeStrictEqualityOps
                                |> normalizeFSharpIfConditionComparisons

    and normalizeNestedParenthesizedFSharpIfs (s: string) : string =
        let mutable result = s
        let mutable idx = result.LastIndexOf("(if ", StringComparison.Ordinal)
        let mutable guard = 0

        while idx >= 0 && guard < 1024 do
            guard <- guard + 1
            match findMatchingParen result idx with
            | Some closeIdx ->
                let segment = result.Substring(idx, closeIdx - idx + 1)
                let inner = result.Substring(idx + 1, closeIdx - idx - 1)
                let replacement = "(" + normalizeCore inner + ")"
                if replacement <> segment then
                    result <- result.Substring(0, idx) + replacement + result.Substring(closeIdx + 1)
                    idx <- result.LastIndexOf("(if ", StringComparison.Ordinal)
                else
                    if idx = 0 then
                        idx <- -1
                    else
                        idx <- result.LastIndexOf("(if ", idx - 1, StringComparison.Ordinal)
            | None ->
                if idx = 0 then
                    idx <- -1
                else
                    idx <- result.LastIndexOf("(if ", idx - 1, StringComparison.Ordinal)

        result

    and normalizeNestedParenthesizedLetIns (s: string) : string =
        let mutable result = s
        let mutable idx = result.LastIndexOf("(let ", StringComparison.Ordinal)
        let mutable guard = 0

        while idx >= 0 && guard < 1024 do
            guard <- guard + 1
            match findMatchingParen result idx with
            | Some closeIdx ->
                let segment = result.Substring(idx, closeIdx - idx + 1)
                let inner = result.Substring(idx + 1, closeIdx - idx - 1)
                let replacementInner =
                    match tryFlattenFSharpLetInChain inner with
                    | Some flattened ->
                        match tryInlineFlattenedLetBindings flattened with
                        | Some inlined -> inlined
                        | None -> inner
                    | None -> normalizeCore inner
                let replacement =
                    if replacementInner.Contains("\n", StringComparison.Ordinal) then
                        replacementInner
                    else
                        "(" + replacementInner + ")"
                if replacement <> segment then
                    result <- result.Substring(0, idx) + replacement + result.Substring(closeIdx + 1)
                    idx <- result.LastIndexOf("(let ", StringComparison.Ordinal)
                else
                    if idx = 0 then
                        idx <- -1
                    else
                        idx <- result.LastIndexOf("(let ", idx - 1, StringComparison.Ordinal)
            | None ->
                if idx = 0 then
                    idx <- -1
                else
                    idx <- result.LastIndexOf("(let ", idx - 1, StringComparison.Ordinal)

        result

    and tryFlattenFSharpLetInChain (s: string) : string option =
        let findTopLevelEq (t: string) (fromIdx: int) : int option =
            let mutable paren = 0
            let mutable bracket = 0
            let mutable brace = 0
            let mutable inString = false
            let mutable quote = '\000'
            let mutable escaped = false
            let mutable i = fromIdx
            let mutable found = -1
            let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString
            let prevNonWsIdx idx =
                let mutable j = idx - 1
                while j >= 0 && Char.IsWhiteSpace(t.[j]) do
                    j <- j - 1
                j
            let nextNonWsIdx idx =
                let mutable j = idx + 1
                while j < t.Length && Char.IsWhiteSpace(t.[j]) do
                    j <- j + 1
                j

            while i < t.Length && found < 0 do
                let c = t.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = quote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        quote <- c
                    | '(' -> paren <- paren + 1
                    | ')' when paren > 0 -> paren <- paren - 1
                    | '[' -> bracket <- bracket + 1
                    | ']' when bracket > 0 -> bracket <- bracket - 1
                    | '{' -> brace <- brace + 1
                    | '}' when brace > 0 -> brace <- brace - 1
                    | '=' when isTopLevel () ->
                        let p = prevNonWsIdx i
                        let n = nextNonWsIdx i
                        let prevC = if p >= 0 then Some t.[p] else None
                        let nextC = if n < t.Length then Some t.[n] else None
                        let composite =
                            match prevC, nextC with
                            | Some ('<' | '>' | '!' | '='), _
                            | _, Some ('=' | '>') -> true
                            | _ -> false
                        if not composite then
                            found <- i
                    | _ -> ()
                i <- i + 1
            if found >= 0 then Some found else None

        let findTopLevelWordIn (t: string) (fromIdx: int) : int option =
            let mutable paren = 0
            let mutable bracket = 0
            let mutable brace = 0
            let mutable inString = false
            let mutable quote = '\000'
            let mutable escaped = false
            let mutable i = fromIdx
            let mutable found = -1
            let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString
            let isBoundary idx =
                idx < 0 || idx >= t.Length || (not (Char.IsLetterOrDigit t.[idx]) && t.[idx] <> '_')

            while i + 1 < t.Length && found < 0 do
                let c = t.[i]
                if inString then
                    if escaped then
                        escaped <- false
                    elif c = '\\' then
                        escaped <- true
                    elif c = quote then
                        inString <- false
                else
                    match c with
                    | '"' | '\'' | '`' ->
                        inString <- true
                        quote <- c
                    | '(' -> paren <- paren + 1
                    | ')' when paren > 0 -> paren <- paren - 1
                    | '[' -> bracket <- bracket + 1
                    | ']' when bracket > 0 -> bracket <- bracket - 1
                    | '{' -> brace <- brace + 1
                    | '}' when brace > 0 -> brace <- brace - 1
                    | 'i' when isTopLevel () && i + 1 < t.Length && t.[i + 1] = 'n' ->
                        if isBoundary (i - 1) && isBoundary (i + 2) then
                            found <- i
                    | _ -> ()
                i <- i + 1
            if found >= 0 then Some found else None

        let trySplitLetIn (expr: string) : (string * string * string) option =
            let t = trimParens (expr.Trim())
            if not (t.StartsWith("let ", StringComparison.Ordinal)) then
                None
            else
                let mutable i = 4
                while i < t.Length && Char.IsWhiteSpace(t.[i]) do
                    i <- i + 1
                let startName = i
                while i < t.Length && (Char.IsLetterOrDigit(t.[i]) || t.[i] = '_' || t.[i] = '\'') do
                    i <- i + 1
                let name = t.Substring(startName, i - startName).Trim()
                if String.IsNullOrWhiteSpace name then
                    None
                else
                    match findTopLevelEq t i with
                    | None -> None
                    | Some eqIdx ->
                        match findTopLevelWordIn t (eqIdx + 1) with
                        | None -> None
                        | Some inIdx ->
                            let rhs = t.Substring(eqIdx + 1, inIdx - eqIdx - 1).Trim()
                            let tail = t.Substring(inIdx + 2).Trim()
                            if String.IsNullOrWhiteSpace rhs || String.IsNullOrWhiteSpace tail then
                                None
                            else
                                Some (name, rhs, tail)

        let rec collectBindings (acc: (string * string) list) (expr: string) : (string * string) list * string =
            match trySplitLetIn expr with
            | Some (name, rhs, tail) ->
                let rhsNorm = normalizeCore rhs
                collectBindings (acc @ [name, rhsNorm]) tail
            | None ->
                acc, expr

        let bindings, finalExprRaw = collectBindings [] s
        if List.isEmpty bindings then
            None
        else
            let finalExpr = normalizeCore finalExprRaw
            let bindingLines =
                bindings
                |> List.map (fun (name, rhs) ->
                    if rhs.Contains("\n", StringComparison.Ordinal) then
                        name + " =\n" + indentBlock rhs
                    else
                        name + " = " + rhs)
            Some (String.concat "\n" (bindingLines @ [finalExpr]))

    let v0 =
        raw
        |> stripTrailingStandaloneExitCode
        |> collapseLeadingDuplicateNumericLine
        |> fun s -> s.Trim().TrimEnd(';').Trim()
    let v0b =
        v0
        |> stripHostCastWrappers
        |> normalizeHostConsoleWrappers
        |> normalizeHostMaybeConstructors
        |> normalizeHostMaybeMatchWrappers
        |> normalizePythonIntDiv
    let v1 = normalizeCore v0b
    let v2 = Regex.Replace(v1, @"\b(-?\d+)L\b", "$1")
    let v3 =
        v2
        |> fun s -> Regex.Replace(s, @"(?<![A-Za-z0-9_])True(?![A-Za-z0-9_])", "true")
        |> fun s -> Regex.Replace(s, @"(?<![A-Za-z0-9_])False(?![A-Za-z0-9_])", "false")
        |> normalizeSimpleTemplateLiterals
    let v4 = normalizeTupleCtorCalls v3
    let v4b = normalizeSimpleTupleCtorCalls v4
    let v5 =
        // Keep `if` directly on the same line as match arrows to preserve
        // indentation semantics for ll-lang's offside parser.
        Regex.Replace(v4b, @"->\s*\r?\n\s*if\s+", "-> if ")
    let v6 =
        // F# backend emits `match ... with | ...`. ll-lang has no `with`;
        // remove it without changing layout/indentation.
        Regex.Replace(v5, @"\bwith\s+\|", "|")
    if String.IsNullOrWhiteSpace v6 then None else Some v6

let private splitComma (raw: string) : string list =
    raw.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun s -> s.Trim())
    |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    |> Array.toList

let private trySplitTopLevelArrow (s: string) : (string * string) option =
    let mutable paren = 0
    let mutable bracket = 0
    let mutable brace = 0
    let mutable inString = false
    let mutable stringQuote = '\000'
    let mutable escaped = false
    let mutable idx = -1
    let isTopLevel () = paren = 0 && bracket = 0 && brace = 0 && not inString

    let mutable i = 0
    while i < s.Length - 1 && idx < 0 do
        let c = s.[i]
        if inString then
            if escaped then
                escaped <- false
            elif c = '\\' then
                escaped <- true
            elif c = stringQuote then
                inString <- false
        else
            match c with
            | '"' | '\'' | '`' ->
                inString <- true
                stringQuote <- c
            | '(' -> paren <- paren + 1
            | ')' when paren > 0 -> paren <- paren - 1
            | '[' -> bracket <- bracket + 1
            | ']' when bracket > 0 -> bracket <- bracket - 1
            | '{' -> brace <- brace + 1
            | '}' when brace > 0 -> brace <- brace - 1
            | '=' when s.[i + 1] = '>' && isTopLevel () ->
                idx <- i
            | '-' when s.[i + 1] = '>' && isTopLevel () ->
                idx <- i
            | _ -> ()
        i <- i + 1

    if idx > 0 then
        let left = s.Substring(0, idx).Trim()
        let right = s.Substring(idx + 2).Trim()
        if String.IsNullOrWhiteSpace left || String.IsNullOrWhiteSpace right then
            None
        else
            Some (left, right)
    else
        None

let private parseLambdaParamSegment (paramParser: string -> string list) (raw: string) : string list =
    let t = raw.Trim()
    let core =
        if t.StartsWith("(") && t.EndsWith(")") && t.Length >= 2 then
            t.Substring(1, t.Length - 2).Trim()
        else
            t
    let parsed = paramParser core
    if List.isEmpty parsed then
        normalizeParamName core |> Option.toList
    else
        parsed

let private tryPeelCurriedArrowLambdas
    (paramParser: string -> string list)
    (raw: string)
    : (string list * string) option =
    let rec loop (expr: string) (acc: string list) =
        match trySplitTopLevelArrow expr with
        | Some (left, right) ->
            let lambdaParams = parseLambdaParamSegment paramParser left
            if List.isEmpty lambdaParams then
                None
            else
                loop (right.Trim()) (acc @ lambdaParams)
        | None ->
            if List.isEmpty acc then
                None
            else
                normalizeRecoveredExpr expr
                |> Option.map (fun body -> acc, body)
    loop (raw.Trim()) []

let private recoverCurriedArrowFns
    (paramParser: string -> string list)
    (fns: ReverseFn list)
    : ReverseFn list =
    fns
    |> List.map (fun fnDecl ->
        match tryPeelCurriedArrowLambdas paramParser fnDecl.Body with
        | Some (extraParams, body) ->
            { fnDecl with
                Params = fnDecl.Params @ extraParams
                Body = body }
        | None ->
            fnDecl)

let private tryParsePythonDefBlockBody
    (outerParams: string list)
    (blockRaw: string)
    : (string list * string) option =
    let parseParamsByColonPrefixLocal (raw: string) : string list =
        splitComma raw
        |> List.choose (fun p ->
            let left =
                let i = p.IndexOf(':')
                if i >= 0 then p.Substring(0, i).Trim() else p.Trim()
            normalizeParamName left)

    let lines =
        blockRaw.Split([| '\n' |], StringSplitOptions.None)
        |> Array.map (fun line -> line.TrimEnd('\r'))
        |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
        |> Array.map (fun line ->
            let indent = line |> Seq.takeWhile (fun c -> c = ' ' || c = '\t') |> Seq.length
            indent, line.Trim())

    let rec parseLevel (idx: int) (indent: int) : (string list * string * int) option =
        if idx >= lines.Length then
            None
        else
            let (lineIndent, text) = lines.[idx]
            if lineIndent <> indent then
                None
            else
                let nestedDefMatch =
                    Regex.Match(
                        text,
                        @"^def\s+[A-Za-z_][A-Za-z0-9_]*\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*$"
                    )
                if nestedDefMatch.Success then
                    let nestedParams = parseParamsByColonPrefixLocal nestedDefMatch.Groups.["params"].Value
                    match parseLevel (idx + 1) (indent + 4) with
                    | Some (innerParams, body, nextIdx)
                        when nextIdx < lines.Length
                             && fst lines.[nextIdx] = indent
                             && Regex.IsMatch(snd lines.[nextIdx], @"^return\s+[A-Za-z_][A-Za-z0-9_]*\s*$") ->
                        Some (nestedParams @ innerParams, body, nextIdx + 1)
                    | _ ->
                        None
                else
                    let returnMatch = Regex.Match(text, @"^return\s+(?<value>[^\r\n#]+)\s*$")
                    if returnMatch.Success then
                        normalizeRecoveredExpr returnMatch.Groups.["value"].Value
                        |> Option.map (fun body -> [], body, idx + 1)
                    else
                        None

    match parseLevel 0 4 with
    | Some (extraParams, body, consumedIdx) when consumedIdx = lines.Length ->
        Some (outerParams @ extraParams, body)
    | _ ->
        None

let private collectPythonDefBlockFns (src: string) : ReverseFn list =
    let parseParamsByColonPrefixLocal (raw: string) : string list =
        splitComma raw
        |> List.choose (fun p ->
            let left =
                let i = p.IndexOf(':')
                if i >= 0 then p.Substring(0, i).Trim() else p.Trim()
            normalizeParamName left)

    Regex.Matches(
        src,
        @"(?ms)^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n(?<block>(?:[ \t]+[^\r\n]*\r?\n)*)"
    )
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
        match normalizeRecoveredName m.Groups.["name"].Value with
        | None -> None
        | Some name ->
            let outerParams = parseParamsByColonPrefixLocal m.Groups.["params"].Value
            match tryParsePythonDefBlockBody outerParams m.Groups.["block"].Value with
            | Some (allParams, body) ->
                Some {
                    Index = m.Index
                    Name = name
                    Params = allParams
                    Body = body
                }
            | None ->
                None)
    |> List.ofSeq
    |> List.sortBy (fun d -> d.Index)
    |> dedupeFns

let private parseFnParamsByLastToken (raw: string) : string list =
    splitComma raw
    |> List.choose (fun p ->
        let tokens = p.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
        if tokens.Length = 0 then None
        else
            let name = tokens[tokens.Length - 1].Trim()
            normalizeParamName name)

let private parseFnParamsByColonPrefix (raw: string) : string list =
    splitComma raw
    |> List.choose (fun p ->
        let left =
            let i = p.IndexOf(':')
            if i >= 0 then p.Substring(0, i).Trim() else p.Trim()
        normalizeParamName left)

let private parseFnParamsByFSharpTyped (raw: string) : string list =
    let matches = Regex.Matches(raw, @"\(\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*[^)]*\)")
    if matches.Count = 0 then
        raw.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose normalizeParamName
        |> Array.toList
    else
        matches
        |> Seq.cast<Match>
        |> Seq.choose (fun m -> normalizeParamName m.Groups.["name"].Value)
        |> List.ofSeq

let private parseFnParamsBySpaceTokens (raw: string) : string list =
    raw.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.choose normalizeParamName
    |> Array.toList

let private parseLlvmParams (raw: string) : string list =
    splitComma raw
    |> List.mapi (fun i p ->
        let m = Regex.Match(p, @"%(?<name>[A-Za-z_][A-Za-z0-9_]*)")
        if m.Success then m.Groups.["name"].Value else $"arg{i}")

let private parseLlvmValueAtom (raw: string) : string option =
    let t = raw.Trim()
    if Regex.IsMatch(t, @"^-?\d+$") then Some t
    else
        let m = Regex.Match(t, @"^%(?<name>[A-Za-z_][A-Za-z0-9_]*)$")
        if m.Success then normalizeRecoveredName m.Groups.["name"].Value
        else None

let private parseLlvmCallArgs (raw: string) : string list =
    splitComma raw
    |> List.choose (fun p ->
        let parts = p.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
        if parts.Length = 0 then None
        else parseLlvmValueAtom parts[parts.Length - 1])

let private collectFnsByRegex
    (src: string)
    (pattern: string)
    (options: RegexOptions)
    (paramParser: string -> string list)
    (bodyBuilder: Match -> string option)
    : ReverseFn list =
    Regex.Matches(src, pattern, options)
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
        match normalizeRecoveredName m.Groups.["name"].Value with
        | None -> None
        | Some name ->
            let paramsRaw =
                if m.Groups.["params"].Success then m.Groups.["params"].Value else ""
            let parameters = paramParser paramsRaw
            match bodyBuilder m with
            | Some body ->
                Some {
                    Index = m.Index
                    Name = name
                    Params = parameters
                    Body = body
                }
            | None -> None)
    |> List.ofSeq
    |> List.sortBy (fun d -> d.Index)
    |> dedupeFns

let private buildIfExprFromReturns (condRaw: string) (thenRaw: string) (elseRaw: string) : string option =
    match normalizeRecoveredExpr condRaw, normalizeRecoveredExpr thenRaw, normalizeRecoveredExpr elseRaw with
    | Some cond, Some thenExpr, Some elseExpr ->
        Some ("if " + cond + "\n  " + thenExpr + "\nelse " + elseExpr)
    | _ -> None

let private buildIfElseIfExprFromReturns
    (cond1Raw: string)
    (then1Raw: string)
    (cond2Raw: string)
    (then2Raw: string)
    (elseRaw: string)
    : string option =
    match buildIfExprFromReturns cond2Raw then2Raw elseRaw with
    | Some nestedElse ->
        match normalizeRecoveredExpr cond1Raw, normalizeRecoveredExpr then1Raw with
        | Some cond1, Some then1 ->
            Some ("if " + cond1 + "\n  " + then1 + "\nelse " + nestedElse)
        | _ -> None
    | None -> None

let private parseIntValue (raw: string) : string option =
    if Regex.IsMatch(raw, @"^-?\d+$") then Some raw else None

let private parseFloatValue (raw: string) : string option =
    let t = raw.Trim()
    let t0 =
        if t.EndsWith("f", StringComparison.OrdinalIgnoreCase)
           || t.EndsWith("d", StringComparison.OrdinalIgnoreCase)
           || t.EndsWith("m", StringComparison.OrdinalIgnoreCase) then
            t.Substring(0, t.Length - 1)
        else
            t
    let hasFloatShape =
        Regex.IsMatch(
            t0,
            @"^-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)$"
        )
    if not hasFloatShape then
        None
    else
        match Double.TryParse(t0, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, f ->
            let s = sprintf "%g" f
            if s.Contains(".") || s.Contains("e") || s.Contains("E") then Some s else Some (s + ".0")
        | _ -> None

let private parseBoolValue (raw: string) : string option =
    match raw.Trim() with
    | "true"
    | "True"
    | "1" -> Some "true"
    | "false"
    | "False"
    | "0" -> Some "false"
    | _ -> None

let private parseStringValue (raw: string) : string option =
    Some (raw |> decodeStringEscapes |> encodeLllString)

let private parseCharValue (raw: string) : string option =
    let decoded = decodeStringEscapes raw
    if decoded.Length = 1 then
        Some (encodeLllChar decoded.[0])
    else
        None

let private parseCharFromI8Value (raw: string) : string option =
    match Int32.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture) with
    | true, n ->
        let b =
            if n >= 0 && n <= 255 then n
            elif n >= -128 && n < 0 then n + 256
            else -1
        if b < 0 then None else Some (encodeLllChar (char b))
    | _ -> None

let private collectByRegex (pattern: string) (valueParser: string -> string option) (src: string) : ReverseDecl list =
    Regex.Matches(src, pattern, RegexOptions.Multiline)
    |> Seq.cast<Match>
    |> Seq.choose (fun m ->
        let nameRaw = m.Groups.["name"].Value.Trim()
        let value = m.Groups.["value"].Value.Trim()
        match normalizeRecoveredName nameRaw, valueParser value with
        | Some name, Some normalized ->
            Some { Index = m.Index; Name = name; Value = normalized }
        | _ -> None)
    |> List.ofSeq
    |> List.sortBy (fun d -> d.Index)
    |> dedupeDecls

let private parseDecls (target: Target) (src: string) : ReverseDecl list =
    let collectMany (collectors: ReverseDecl list list) : ReverseDecl list =
        collectors
        |> List.concat
        |> List.sortBy (fun d -> d.Index)
        |> dedupeDecls

    match target with
    | FSharp ->
        collectMany
            [ collectByRegex @"^\s*let\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)L?\s*$" parseIntValue src
              collectByRegex @"^\s*let\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+))\s*$" parseFloatValue src
              collectByRegex @"^\s*let\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>true|false)\s*$" parseBoolValue src
              collectByRegex @"^\s*let\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>(?:[^'\\]|\\.)*)'\s*$" parseCharValue src
              collectByRegex @"^\s*let\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*$" parseStringValue src ]
    | TypeScript ->
        collectMany
            [ collectByRegex @"^\s*(?:export\s+)?(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=;]+)?\s*=\s*(?<value>-?\d+)\s*;?\s*$" parseIntValue src
              collectByRegex @"^\s*(?:export\s+)?(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=;]+)?\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+))\s*;?\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:export\s+)?(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=;]+)?\s*=\s*(?<value>true|false)\s*;?\s*$" parseBoolValue src
              collectByRegex @"^\s*(?:export\s+)?(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=;]+)?\s*=\s*`(?<value>(?:[^`\\]|\\.)*)`\s*;?\s*$" parseStringValue src
              collectByRegex @"^\s*(?:export\s+)?(?:const|let)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=;]+)?\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;?\s*$" parseStringValue src ]
    | Python ->
        collectMany
            [ collectByRegex @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=]+)?\s*=\s*(?<value>-?\d+)\s*$" parseIntValue src
              collectByRegex @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=]+)?\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+))\s*$" parseFloatValue src
              collectByRegex @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=]+)?\s*=\s*(?<value>True|False|true|false)\s*$" parseBoolValue src
              collectByRegex @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=]+)?\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*$" parseStringValue src
              collectByRegex @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[^=]+)?\s*=\s*'(?<value>(?:[^'\\]|\\.)*)'\s*$" parseStringValue src ]
    | CSharp ->
        collectMany
            [ collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+long\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)L\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+long\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+int\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+int\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+double\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfFmM]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+double\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfFmM]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+float\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfFmM]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+float\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfFmM]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+bool\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>true|false)\s*;\s*$" parseBoolValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+bool\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>true|false)\s*;\s*$" parseBoolValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+char\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>(?:[^'\\]|\\.)*)'\s*;\s*$" parseCharValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+char\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>(?:[^'\\]|\\.)*)'\s*;\s*$" parseCharValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+static\s+readonly\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;\s*$" parseStringValue src
              collectByRegex @"^\s*(?:public|private|internal|protected)\s+const\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;\s*$" parseStringValue src ]
    | LLVM ->
        collectMany
            [ collectByRegex @"^\s*@(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*global\s+i64\s+(?<value>-?\d+)\s*$" parseIntValue src
              collectByRegex @"^\s*@(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*global\s+double\s+(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+))\s*$" parseFloatValue src
              collectByRegex @"^\s*@(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*global\s+i1\s+(?<value>[01])\s*$" parseBoolValue src
              collectByRegex @"^\s*@(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*global\s+i8\s+(?<value>-?\d+)\s*$" parseCharFromI8Value src ]
    | Java ->
        collectMany
            [ collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+long\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)L\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+int\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?\d+)\s*;\s*$" parseIntValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+double\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfF]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+float\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>-?(?:(?:\d+\.\d*|\d*\.\d+)(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)[dDfF]?)\s*;\s*$" parseFloatValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+boolean\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>true|false)\s*;\s*$" parseBoolValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+char\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*'(?<value>(?:[^'\\]|\\.)*)'\s*;\s*$" parseCharValue src
              collectByRegex @"^\s*(?:public|private|protected)\s+static\s+final\s+String\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;\s*$" parseStringValue src ]

let private normalizeTypeDeclName (raw: string) : string option =
    let n = raw.Trim()
    if idRx.IsMatch(n) then Some n else None

let private splitTopLevelBy (sep: char) (raw: string) : string list =
    let parts = ResizeArray<string>()
    let mutable start = 0
    let mutable paren = 0
    let mutable bracket = 0
    let mutable brace = 0
    let mutable angle = 0
    let mutable inString = false
    let mutable quote = '\000'
    let mutable escaped = false
    let mutable i = 0
    while i < raw.Length do
        let c = raw.[i]
        if inString then
            if escaped then
                escaped <- false
            elif c = '\\' then
                escaped <- true
            elif c = quote then
                inString <- false
        else
            match c with
            | '"' | '`' ->
                inString <- true
                quote <- c
            | '(' -> paren <- paren + 1
            | ')' when paren > 0 -> paren <- paren - 1
            | '[' -> bracket <- bracket + 1
            | ']' when bracket > 0 -> bracket <- bracket - 1
            | '{' -> brace <- brace + 1
            | '}' when brace > 0 -> brace <- brace - 1
            | '<' -> angle <- angle + 1
            | '>' when angle > 0 -> angle <- angle - 1
            | _ -> ()

            if c = sep && paren = 0 && bracket = 0 && brace = 0 && angle = 0 then
                parts.Add(raw.Substring(start, i - start))
                start <- i + 1
        i <- i + 1

    parts.Add(raw.Substring(start))
    parts
    |> Seq.map (fun x -> x.Trim())
    |> Seq.filter (fun x -> not (String.IsNullOrWhiteSpace x))
    |> List.ofSeq

let private stripOuterParensSimple (raw: string) : string =
    let mutable t = raw.Trim()
    let mutable changed = true
    while changed && t.Length >= 2 && t.StartsWith("(") && t.EndsWith(")") do
        let mutable depth = 0
        let mutable ok = true
        let mutable i = 0
        while i < t.Length && ok do
            match t.[i] with
            | '(' -> depth <- depth + 1
            | ')' ->
                depth <- depth - 1
                if depth = 0 && i < t.Length - 1 then
                    ok <- false
            | _ -> ()
            i <- i + 1
        if ok && depth = 0 then
            t <- t.Substring(1, t.Length - 2).Trim()
            changed <- true
        else
            changed <- false
    t

let private parseFSharpTypeParams (raw: string) : string list =
    let t = raw.Trim()
    if String.IsNullOrWhiteSpace t then
        []
    else
        let core =
            if t.StartsWith("<") && t.EndsWith(">") && t.Length >= 2 then
                t.Substring(1, t.Length - 2)
            else
                t
        splitTopLevelBy ',' core
        |> List.choose (fun p ->
            let n = p.Trim().TrimStart('\'')
            if idRx.IsMatch(n) then Some n else None)

let private normalizeFSharpPrimitiveType (raw: string) : string =
    match raw.Trim() with
    | "string" -> "Str"
    | "int"
    | "int64"
    | "long" -> "Int"
    | "float"
    | "double" -> "Float"
    | "bool" -> "Bool"
    | "char" -> "Char"
    | "unit" -> "Unit"
    | other -> other

let rec private normalizeFSharpTypeExpr (raw: string) : string =
    let t = stripOuterParensSimple raw
    if String.IsNullOrWhiteSpace t then
        ""
    else
        let baseMapped = normalizeFSharpPrimitiveType t
        if baseMapped <> t then
            baseMapped
        elif t.StartsWith("'") then
            t.TrimStart('\'')
        elif t.EndsWith(" list", StringComparison.Ordinal) then
            let inner = t.Substring(0, t.Length - " list".Length).Trim()
            let innerNorm = normalizeFSharpTypeExpr inner
            if String.IsNullOrWhiteSpace innerNorm then t else "List[" + innerNorm + "]"
        elif t.EndsWith(" option", StringComparison.Ordinal) then
            let inner = t.Substring(0, t.Length - " option".Length).Trim()
            let innerNorm = normalizeFSharpTypeExpr inner
            if String.IsNullOrWhiteSpace innerNorm then t else "Maybe[" + innerNorm + "]"
        else
            let m = Regex.Match(t, @"^(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*<(?<args>.+)>$")
            if m.Success then
                let name = m.Groups.["name"].Value
                let args =
                    splitTopLevelBy ',' m.Groups.["args"].Value
                    |> List.map normalizeFSharpTypeExpr
                    |> List.filter (fun a -> not (String.IsNullOrWhiteSpace a))
                if List.isEmpty args then
                    name
                else
                    name + "[" + String.concat " " args + "]"
            else
                t

let private normalizeFSharpCtorPayload (raw: string) : string =
    splitTopLevelBy '*' raw
    |> List.map normalizeFSharpTypeExpr
    |> List.filter (fun t -> not (String.IsNullOrWhiteSpace t))
    |> String.concat " "

let private tryRecoverFSharpTypeDecl (m: Match) : ReverseTypeDecl option =
    match normalizeTypeDeclName m.Groups.["name"].Value with
    | None -> None
    | Some name ->
        let typeParams = parseFSharpTypeParams m.Groups.["gen"].Value
        let bodyRaw = m.Groups.["body"].Value.Replace("\r\n", "\n").Trim()
        let header =
            if List.isEmpty typeParams then name
            else name + " " + String.concat " " typeParams

        // F# backend maps Maybe<'A> to option. Recover source-level sum.
        let tryRecoverMaybeAlias () =
            if name = "Maybe" && typeParams.Length = 1 then
                let p = typeParams.Head
                if Regex.IsMatch(bodyRaw, @"^'?[A-Za-z_][A-Za-z0-9_]*\s+option$") then
                    Some {
                        Index = m.Index
                        Name = name
                        Body = header + " = Some " + p + " | None"
                    }
                else
                    None
            else
                None

        match tryRecoverMaybeAlias () with
        | Some recovered -> Some recovered
        | None ->
            let ctorLines =
                let lines =
                    bodyRaw.Split('\n')
                    |> Array.map (fun l -> l.Trim())
                    |> Array.filter (fun l -> l.StartsWith("|", StringComparison.Ordinal))
                    |> Array.toList
                if not (List.isEmpty lines) then
                    lines
                else
                    if bodyRaw.Contains("|", StringComparison.Ordinal) then
                        splitTopLevelBy '|' bodyRaw |> List.map (fun seg -> "| " + seg)
                    else
                        []

            let ctors =
                ctorLines
                |> List.choose (fun line ->
                    let cm = Regex.Match(line, @"^\|\s*(?<ctor>[A-Za-z_][A-Za-z0-9_]*)(?:\s+of\s+(?<payload>.+))?$")
                    if not cm.Success then
                        None
                    else
                        let ctor = cm.Groups.["ctor"].Value
                        if cm.Groups.["payload"].Success then
                            let payload = normalizeFSharpCtorPayload cm.Groups.["payload"].Value
                            if String.IsNullOrWhiteSpace payload then
                                Some ("| " + ctor)
                            else
                                Some ("| " + ctor + " " + payload)
                        else
                            Some ("| " + ctor))

            if List.isEmpty ctors then
                None
            else
                Some {
                    Index = m.Index
                    Name = name
                    Body = header + " =\n" + (ctors |> List.map (fun c -> "  " + c) |> String.concat "\n")
                }

let private parseTypeDecls (target: Target) (src: string) : ReverseTypeDecl list =
    match target with
    | FSharp ->
        Regex.Matches(
            src,
            @"(?ms)^(?:type|and)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<gen>\s*<[^>]+>)?\s*=\s*(?<body>.*?)(?=^(?:type|and)\s+[A-Za-z_][A-Za-z0-9_]*|^\s*let\s+|^\s*\[<|^\s*module\s+|\z)"
        )
        |> Seq.cast<Match>
        |> Seq.choose tryRecoverFSharpTypeDecl
        |> List.ofSeq
        |> List.sortBy (fun d -> d.Index)
        |> dedupeTypes
    | _ -> []

let private parseFunctions (target: Target) (src: string) : ReverseFn list =
    let body = afterPreludeSection src
    let collect options pattern paramParser bodyBuilder =
        collectFnsByRegex body pattern options paramParser bodyBuilder

    match target with
    | FSharp ->
        let letFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?ms)^let\s+(?:rec\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<params>(?:\s+\([^\)]*\)|\s+[a-z_][A-Za-z0-9_]*)*)\s*(?::\s*[^=]+)?=\s*(?<value>.*?)(?=^(?:and\s+|let\s+)|^\[<|^type\s+|^module\s+|\z)"
                parseFnParamsByFSharpTyped
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let andFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?ms)^and\s+(?<name>[a-z_][A-Za-z0-9_]*)(?<params>(?:\s+\([^\)]*\)|\s+[a-z_][A-Za-z0-9_]*)*)\s*(?::\s*[^=]+)?=\s*(?<value>.*?)(?=^(?:and\s+|let\s+)|^\[<|^type\s+|^module\s+|\z)"
                parseFnParamsByFSharpTyped
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        (letFns @ andFns)
        |> List.sortBy (fun d -> d.Index)
        |> dedupeFns
    | TypeScript ->
        let ifElseIfThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*(?::\s*[^{]+)?\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^\r\n;]+);?\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^\r\n;]+);?\s*\}?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseIfElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*(?::\s*[^{]+)?\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^\r\n;]+);?\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^\r\n;]+);?\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\s*\)\s*(?::\s*[^{]+)?\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^\r\n;]+);?\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifReturnThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*(?::\s*[^{]+)?\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^\r\n;]+);?\s*\}?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifReturnThenFallbackReturnArrowFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^\r\n;]+);?\s*\}?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseArrowBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^\r\n;]+);?\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseIfElseArrowBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^\r\n;]+);?\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^\r\n;]+);?\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseIfThenFallbackReturnArrowFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^\r\n;]+);?\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^\r\n;]+);?\s*\}?\s*return\s+(?<elseValue>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let tsMatchIifeFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?ms)^\s*(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\(\(\)\s*=>\s*\{\s*if\s*\(\s*(?<var>[A-Za-z_][A-Za-z0-9_]*)\?\._tag\s*===\s*[`""]Some[`""]\s*\)\s*\{\s*const\s+(?<bind>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[^;]+;\s*return\s+(?<ret>[A-Za-z_][A-Za-z0-9_]*)\s*;\s*\}\s*if\s*\(\s*(?<var2>[A-Za-z_][A-Za-z0-9_]*)\?\._tag\s*===\s*[`""]None[`""]\s*\)\s*\{\s*return\s+(?<noneRet>[^\r\n;]+)\s*;\s*\}\s*throw\s+new\s+Error\([^\)]*\)\s*;\s*\}\)\(\)\s*;\s*$"
                parseFnParamsByColonPrefix
                (fun m ->
                    let var1 = m.Groups.["var"].Value.Trim()
                    let var2 = m.Groups.["var2"].Value.Trim()
                    let bind = m.Groups.["bind"].Value.Trim()
                    let ret = m.Groups.["ret"].Value.Trim()
                    let noneRet = m.Groups.["noneRet"].Value.Trim()
                    if String.IsNullOrWhiteSpace var1
                       || not (String.Equals(var1, var2, StringComparison.Ordinal))
                       || not (String.Equals(bind, ret, StringComparison.Ordinal))
                       || String.IsNullOrWhiteSpace noneRet then
                        None
                    else
                        Some (sprintf "match %s | Some(%s) -> %s | None -> %s" var1 bind ret noneRet))
        let arrowFns =
            collect
                RegexOptions.Multiline
                @"^\s*(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*(?<value>[^\r\n;]+);?\s*$"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let arrowSingleParamFns =
            collect
                RegexOptions.Multiline
                @"^\s*(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<params>[A-Za-z_][A-Za-z0-9_]*(?:\s*:\s*[^=]+)?)\s*=>\s*(?<value>[^\r\n;]+);?\s*$"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let arrowBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?const\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\((?<params>[^)]*)\)\s*=>\s*\{[\s\S]*?return\s+(?<value>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let blockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*(?::\s*[^{]+)?\s*\{[\s\S]*?return\s+(?<value>[^\r\n;]+);?\s*\}"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        (ifElseIfThenFallbackReturnFns
         @ ifElseIfThenFallbackReturnArrowFns
         @ ifElseIfElseBlockFns
         @ ifElseIfElseArrowBlockFns
         @ ifElseBlockFns
         @ ifElseArrowBlockFns
         @ ifReturnThenFallbackReturnFns
         @ ifReturnThenFallbackReturnArrowFns
         @ tsMatchIifeFns
         @ arrowFns
         @ arrowSingleParamFns
         @ arrowBlockFns
         @ blockFns)
        |> recoverCurriedArrowFns parseFnParamsByColonPrefix
        |> List.sortBy (fun d -> d.Index)
        |> dedupeFns
    | Python ->
        let blockStyleFns = collectPythonDefBlockFns body
        let ifElifThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n\s*if\s+(?<cond1>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<then1>[^\r\n#]+)\s*\r?\n\s*elif\s+(?<cond2>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<then2>[^\r\n#]+)\s*\r?\n\s*return\s+(?<elseValue>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElifElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n\s*if\s+(?<cond1>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<then1>[^\r\n#]+)\s*\r?\n\s*elif\s+(?<cond2>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<then2>[^\r\n#]+)\s*\r?\n\s*else\s*:\s*\r?\n\s*return\s+(?<elseValue>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n\s*if\s+(?<cond>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<thenValue>[^\r\n#]+)\s*\r?\n\s*else\s*:\s*\r?\n\s*return\s+(?<elseValue>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifReturnThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n\s*if\s+(?<cond>[^\r\n:]+)\s*:\s*\r?\n\s*return\s+(?<thenValue>[^\r\n#]+)\s*\r?\n\s*return\s+(?<elseValue>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let blockFns =
            collect
                RegexOptions.Multiline
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*\r?\n\s*return\s+(?<value>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let singleLineFns =
            collect
                RegexOptions.Multiline
                @"^def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^\)]*)\)\s*(?:->\s*[^:]+)?\s*:\s*return\s+(?<value>[^\r\n#]+)\s*$"
                parseFnParamsByColonPrefix
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        (blockStyleFns
         @ ifElifThenFallbackReturnFns
         @ ifElifElseBlockFns
         @ ifElseBlockFns
         @ ifReturnThenFallbackReturnFns
         @ blockFns
         @ singleLineFns)
        |> List.sortBy (fun d -> d.Index)
        |> dedupeFns
    | CSharp ->
        let ifElseIfThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^;]+);\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^;]+);\s*\}?\s*return\s+(?<elseValue>[^;]+);\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseIfElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^;]+);\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^;]+);\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^;]+);\s*\}?\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^;]+);\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^;]+);\s*\}?\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifReturnThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^;]+);\s*\}?\s*return\s+(?<elseValue>[^;]+);\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let csharpVoidSingleStmtFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+void\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?(?<value>[A-Za-z_][A-Za-z0-9_\.]*\s*\([^\)]*\))\s*;\s*[\s\S]*?\}"
                parseFnParamsByLastToken
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let exprBodied =
            collect
                RegexOptions.Multiline
                @"^\s*(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*=>\s*(?<value>.+);\s*$"
                parseFnParamsByLastToken
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let blockBodied =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|internal|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?return\s+(?<value>[^;]+);\s*\}"
                parseFnParamsByLastToken
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        (ifElseIfThenFallbackReturnFns
         @ ifElseIfElseBlockFns
         @ ifElseBlockFns
         @ ifReturnThenFallbackReturnFns
         @ csharpVoidSingleStmtFns
         @ exprBodied
         @ blockBodied)
        |> recoverCurriedArrowFns parseFnParamsByLastToken
        |> List.sortBy (fun d -> d.Index)
        |> dedupeFns
    | LLVM ->
        let maybeMatchPhiFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{(?<fnBody>[\s\S]*?)^\}"
                parseLlvmParams
                (fun m ->
                    let fnBody = m.Groups.["fnBody"].Value
                    let scrutMatch =
                        Regex.Match(
                            fnBody,
                            @"icmp\s+ne\s+ptr\s+%(?<scrut>[A-Za-z_][A-Za-z0-9_]*),\s+null",
                            RegexOptions.Singleline
                        )
                    let hasTag1 =
                        Regex.IsMatch(
                            fnBody,
                            @"icmp\s+eq\s+i64\s+%[A-Za-z_][A-Za-z0-9_]*,\s+1",
                            RegexOptions.Singleline
                        )
                    let hasTag2 =
                        Regex.IsMatch(
                            fnBody,
                            @"icmp\s+eq\s+i64\s+%[A-Za-z_][A-Za-z0-9_]*,\s+2",
                            RegexOptions.Singleline
                        )
                    let phiMatch =
                        Regex.Match(
                            fnBody,
                            @"%\w+\s*=\s*phi\s+i64\s+\[\s*%(?<payload>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*%match_body_[0-9]+\s*\],\s*\[\s*(?<none1>-?\d+)\s*,\s*%match_body_[0-9]+\s*\],\s*\[\s*(?<none2>-?\d+)\s*,\s*%match_fail_[0-9]+\s*\]",
                            RegexOptions.Singleline
                        )
                    if not scrutMatch.Success || not hasTag1 || not hasTag2 || not phiMatch.Success then
                        None
                    else
                        let scrut = scrutMatch.Groups.["scrut"].Value.Trim()
                        let payload = phiMatch.Groups.["payload"].Value.Trim()
                        let none1 = phiMatch.Groups.["none1"].Value.Trim()
                        let none2 = phiMatch.Groups.["none2"].Value.Trim()
                        let noneExpr =
                            if String.IsNullOrWhiteSpace none2 || String.Equals(none1, none2, StringComparison.Ordinal) then
                                none1
                            else
                                none1
                        if String.IsNullOrWhiteSpace scrut
                           || String.IsNullOrWhiteSpace payload
                           || String.IsNullOrWhiteSpace noneExpr then
                            None
                        else
                            Some (sprintf "match %s | Some(n) -> n | None -> %s" scrut noneExpr))
        let addFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?%\w+\s*=\s*add\s+i64\s+%(?<lhs>[A-Za-z_][A-Za-z0-9_]*),\s*(?<rhs>-?\d+)[\s\S]*?ret\s+i64\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m ->
                    let lhs = m.Groups.["lhs"].Value
                    let rhs = m.Groups.["rhs"].Value
                    Some (lhs + " + " + rhs))
        let subFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?%\w+\s*=\s*sub\s+i64\s+%(?<lhs>[A-Za-z_][A-Za-z0-9_]*),\s*(?<rhs>-?\d+)[\s\S]*?ret\s+i64\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m ->
                    let lhs = m.Groups.["lhs"].Value
                    let rhs = m.Groups.["rhs"].Value
                    Some (lhs + " - " + rhs))
        let mulFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?%\w+\s*=\s*mul\s+i64\s+%(?<lhs>[A-Za-z_][A-Za-z0-9_]*),\s*(?<rhs>-?\d+)[\s\S]*?ret\s+i64\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m ->
                    let lhs = m.Groups.["lhs"].Value
                    let rhs = m.Groups.["rhs"].Value
                    Some (lhs + " * " + rhs))
        let divFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?%\w+\s*=\s*sdiv\s+i64\s+%(?<lhs>[A-Za-z_][A-Za-z0-9_]*),\s*(?<rhs>-?\d+)[\s\S]*?ret\s+i64\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m ->
                    let lhs = m.Groups.["lhs"].Value
                    let rhs = m.Groups.["rhs"].Value
                    Some (lhs + " / " + rhs))
        let retArgFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?ret\s+i64\s+%(?<value>[A-Za-z_][A-Za-z0-9_]*)[\s\S]*?\}"
                parseLlvmParams
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let callFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?%\w+\s*=\s*call\s+i64\s+@(?<callee>[A-Za-z_][A-Za-z0-9_]*)\((?<args>[^)]*)\)[\s\S]*?ret\s+i64\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m ->
                    match normalizeRecoveredName m.Groups.["callee"].Value with
                    | None -> None
                    | Some callee ->
                        let args = parseLlvmCallArgs m.Groups.["args"].Value
                        if List.isEmpty args then Some callee
                        else Some (String.concat " " (callee :: args)))
        let retConstFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i64\s+@(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)\s*\{[\s\S]*?ret\s+i64\s+(?<value>-?\d+)[\s\S]*?\}"
                parseLlvmParams
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let mainI32RetConstFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i32\s+@(?<name>main)\((?<params>[^)]*)\)\s*\{[\s\S]*?ret\s+i32\s+(?<value>-?\d+)[\s\S]*?\}"
                parseLlvmParams
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let mainI32TruncConstFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"define\s+i32\s+@(?<name>main)\((?<params>[^)]*)\)\s*\{[\s\S]*?trunc\s+i64\s+(?<value>-?\d+)\s+to\s+i32[\s\S]*?ret\s+i32\s+%\w+[\s\S]*?\}"
                parseLlvmParams
                (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        let recoveredNames =
            maybeMatchPhiFns
            |> List.map (fun f -> f.Name)
            |> Set.ofList
        let retArgFnsFiltered =
            retArgFns
            |> List.filter (fun f -> not (Set.contains f.Name recoveredNames))
        (addFns
         @ subFns
         @ mulFns
         @ divFns
         @ callFns
         @ maybeMatchPhiFns
         @ retArgFnsFiltered
         @ retConstFns
         @ mainI32RetConstFns
         @ mainI32TruncConstFns)
        |> List.sortBy (fun d -> d.Index)
        |> dedupeFns
    | Java ->
        let ifElseIfThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^;]+);\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^;]+);\s*\}?\s*return\s+(?<elseValue>[^;]+);\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseIfElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond1>[^\)]*)\)\s*\{?\s*return\s+(?<then1>[^;]+);\s*\}?\s*else\s+if\s*\((?<cond2>[^\)]*)\)\s*\{?\s*return\s+(?<then2>[^;]+);\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^;]+);\s*\}?\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfElseIfExprFromReturns
                        m.Groups.["cond1"].Value
                        m.Groups.["then1"].Value
                        m.Groups.["cond2"].Value
                        m.Groups.["then2"].Value
                        m.Groups.["elseValue"].Value)
        let ifElseBlockFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^;]+);\s*\}?\s*else\s*\{?\s*return\s+(?<elseValue>[^;]+);\s*\}?\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let ifReturnThenFallbackReturnFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?if\s*\((?<cond>[^\)]*)\)\s*\{?\s*return\s+(?<thenValue>[^;]+);\s*\}?\s*return\s+(?<elseValue>[^;]+);\s*\}"
                parseFnParamsByLastToken
                (fun m ->
                    buildIfExprFromReturns
                        m.Groups.["cond"].Value
                        m.Groups.["thenValue"].Value
                        m.Groups.["elseValue"].Value)
        let javaMainVoidWrapperFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+void\s+(?<name>main)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?var\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?<value>-?\d+L?);\s*[\s\S]*?\}"
                parseFnParamsByLastToken
                (fun m ->
                    normalizeRecoveredExpr m.Groups.["value"].Value)
        let javaVoidSingleStmtFns =
            collect
                (RegexOptions.Multiline ||| RegexOptions.Singleline)
                @"(?:(?:public|private|protected)\s+)?static\s+void\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?(?<value>[A-Za-z_][A-Za-z0-9_\.]*\s*\([^\)]*\))\s*;\s*[\s\S]*?\}"
                parseFnParamsByLastToken
                (fun m ->
                    normalizeRecoveredExpr m.Groups.["value"].Value)
        collect
            (RegexOptions.Multiline ||| RegexOptions.Singleline)
            @"(?:(?:public|private|protected)\s+)?static\s+[A-Za-z0-9_<>,\[\]\.? ]+\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:<[^>]+>)?\((?<params>[^\)]*)\)\s*\{[\s\S]*?return\s+(?<value>[^;]+);\s*\}"
            parseFnParamsByLastToken
            (fun m -> normalizeRecoveredExpr m.Groups.["value"].Value)
        |> fun plain ->
            (ifElseIfThenFallbackReturnFns
             @ ifElseIfElseBlockFns
             @ ifElseBlockFns
             @ ifReturnThenFallbackReturnFns
             @ javaMainVoidWrapperFns
             @ javaVoidSingleStmtFns
             @ plain)
            |> recoverCurriedArrowFns parseFnParamsByLastToken
            |> List.sortBy (fun d -> d.Index)
            |> dedupeFns

let private renderFunctionDecl (fnDecl: ReverseFn) : string =
    let paramSig =
        if List.isEmpty fnDecl.Params then
            "()"
        else
            fnDecl.Params
            |> List.map (fun p -> $"({p})")
            |> String.concat ""
    let body = fnDecl.Body.Trim()
    if body.Contains("\n", StringComparison.Ordinal) then
        let firstLine =
            let i = body.IndexOf('\n')
            if i >= 0 then body.Substring(0, i).Trim() else body
        if firstLine.StartsWith("if ", StringComparison.Ordinal) then
            // Keep historical one-line `= if ...` formatting for compatibility
            // with existing expectations and parser behavior.
            $"{fnDecl.Name}{paramSig} = {body}"
        else
            let indented =
                body.Replace("\r\n", "\n")
                    .Split('\n')
                |> Array.map (fun line -> "  " + line)
                |> String.concat "\n"
            $"{fnDecl.Name}{paramSig} =\n{indented}"
    else
        $"{fnDecl.Name}{paramSig} = {body}"

let reverseToLll (target: Target) (src: string) : Result<string, string> =
    let moduleName = inferModuleName target src
    let typeDeclsRaw = parseTypeDecls target src
    let decls = parseDecls target src
    let fnsRaw = parseFunctions target src
    let hasCtorMarker (text: string) =
        Regex.IsMatch(text, @"\b(Some|None)\b")
    let hasMaybeCtors =
        decls |> List.exists (fun d -> hasCtorMarker d.Value)
        || fnsRaw |> List.exists (fun f -> hasCtorMarker f.Body)
    let hasMaybeType =
        typeDeclsRaw
        |> List.exists (fun t -> String.Equals(t.Name, "Maybe", StringComparison.Ordinal))
    let synthesizedMaybeTypeDecl =
        if hasMaybeCtors && not hasMaybeType then
            [ { Index = Int32.MinValue; Name = "Maybe"; Body = "Maybe A = Some A | None" } ]
        else
            []
    let typeDecls =
        synthesizedMaybeTypeDecl @ typeDeclsRaw
        |> List.sortBy (fun d -> d.Index)
        |> dedupeTypes
    let declNames =
        decls
        |> List.map (fun d -> d.Name)
        |> Set.ofList
    let typeNames =
        typeDecls
        |> List.map (fun d -> d.Name)
        |> Set.ofList
    let fns =
        fnsRaw
        |> List.filter (fun f -> not (Set.contains f.Name declNames) && not (Set.contains f.Name typeNames))
    if List.isEmpty typeDecls && List.isEmpty decls && List.isEmpty fns then
        Error $"reverse parser ({target}) could not recover type declarations, let-bindings, or function declarations"
    else
        let typeBody =
            typeDecls
            |> List.map (fun d -> d.Body)
        let letsBody =
            decls
            |> List.map (fun d -> "let " + d.Name + " = " + d.Value)
        let fnBody =
            fns
            |> List.map renderFunctionDecl
        let body =
            typeBody @ letsBody @ fnBody
            |> String.concat "\n\n"
        Ok ("module " + moduleName + "\n\n" + body + "\n")
