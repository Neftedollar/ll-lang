module LLLang.Manifest

// Hand-written TOML-subset parser for ll.toml project manifest files.
// Supports: [table] headers, key = "value" strings,
//           key = ["a", "b", ...] string arrays, # comment lines.
// Unknown keys / tables are silently ignored (forward compatibility).

type DepSource =
    | GitDep of url: string * ref: string
    | PathDep of path: string

type LLManifest = {
    Name     : string                  // project.name — required
    Version  : string                  // project.version — default "0.0.0"
    Entry    : string                  // project.entry — default "src/Main.lll"
    Deps     : Map<string, DepSource>  // [deps] name → source
    Platform : string list             // [platform] use = [...]
}

// ---- Tokeniser helpers ---------------------------------------------------------

let private isWhitespace (c: char) = c = ' ' || c = '\t' || c = '\r'

let private trimLine (s: string) =
    // Strip inline comment (# outside of quoted strings), then trim
    let mutable i = 0
    let mutable inQuote = false
    let mutable commentStart = -1
    while i < s.Length && commentStart < 0 do
        match s.[i] with
        | '"' -> inQuote <- not inQuote; i <- i + 1
        | '\\' when inQuote && i + 1 < s.Length -> i <- i + 2  // skip escape
        | '#' when not inQuote -> commentStart <- i
        | _ -> i <- i + 1
    let noComment = if commentStart >= 0 then s.[..commentStart - 1] else s
    noComment.Trim()

// Parse a quoted string value starting at index `i` (which should point at
// the opening `"`). Returns (parsed string, index after closing `"`), or
// Error if the closing quote is missing.
let private parseQuotedString (s: string) (i: int) : Result<string * int, string> =
    if i >= s.Length || s.[i] <> '"' then
        Error $"Expected '\"' at position {i}"
    else
        let mutable j = i + 1
        let sb = System.Text.StringBuilder()
        let mutable finished = false
        while not finished && j < s.Length do
            match s.[j] with
            | '"' ->
                j <- j + 1
                finished <- true
            | '\\' when j + 1 < s.Length ->
                match s.[j + 1] with
                | '"'  -> sb.Append('"')  |> ignore; j <- j + 2
                | '\\' -> sb.Append('\\') |> ignore; j <- j + 2
                | 'n'  -> sb.Append('\n') |> ignore; j <- j + 2
                | 't'  -> sb.Append('\t') |> ignore; j <- j + 2
                | 'r'  -> sb.Append('\r') |> ignore; j <- j + 2
                | other ->
                    sb.Append('\\') |> ignore
                    sb.Append(other) |> ignore
                    j <- j + 2
            | c ->
                sb.Append(c) |> ignore
                j <- j + 1
        if not finished then Error "Unterminated string literal"
        else Ok (sb.ToString(), j)

// Parse a string array value: ["a", "b", "c"]
// Assumes the leading '[' is at position i.
let private parseStringArray (s: string) (i: int) : Result<string list * int, string> =
    if i >= s.Length || s.[i] <> '[' then
        Error $"Expected '[' at position {i}"
    else
        let mutable j = i + 1
        let items = System.Collections.Generic.List<string>()
        let mutable finished = false
        let mutable error: string option = None
        while not finished && j < s.Length && error.IsNone do
            // skip whitespace and commas
            while j < s.Length && (isWhitespace s.[j] || s.[j] = ',') do
                j <- j + 1
            if j >= s.Length then
                error <- Some "Unterminated array"
            elif s.[j] = ']' then
                j <- j + 1
                finished <- true
            elif s.[j] = '"' then
                match parseQuotedString s j with
                | Ok (v, j') ->
                    items.Add(v)
                    j <- j'
                | Error e -> error <- Some e
            else
                error <- Some $"Unexpected character '{s.[j]}' in array at position {j}"
        match error with
        | Some e -> Error e
        | None -> Ok (List.ofSeq items, j)

// ---- Line-by-line parser -------------------------------------------------------

// Parse an inline table value like { path = "../ll-json" }
// Returns Some path if it contains a path key, else None.
let private parseInlineTablePath (s: string) : string option =
    // Find "path" key followed by = and a quoted string
    let pathIdx = s.IndexOf("path")
    if pathIdx < 0 then None
    else
        let afterPath = s.[pathIdx + 4..].TrimStart()
        if afterPath.Length = 0 || afterPath.[0] <> '=' then None
        else
            let afterEq = afterPath.[1..].TrimStart()
            if afterEq.Length = 0 || afterEq.[0] <> '"' then None
            else
                match parseQuotedString afterEq 0 with
                | Ok (v, _) -> Some v
                | Error _ -> None

/// Parse a dep value string into a DepSource.
/// "https://github.com/user/repo#v1.0.0" → GitDep("url", "v1.0.0")
/// "https://github.com/user/repo"        → GitDep("url", "main")
/// "{ path = \"../ll-json\" }"           → PathDep("../ll-json")
let private parseDepSource (valueRaw: string) : DepSource option =
    if valueRaw.StartsWith("{") then
        // Inline table
        match parseInlineTablePath valueRaw with
        | Some p -> Some (PathDep p)
        | None -> None
    elif valueRaw.StartsWith("\"") then
        match parseQuotedString valueRaw 0 with
        | Error _ -> None
        | Ok (url, _) ->
            let hashIdx = url.IndexOf('#')
            if hashIdx >= 0 then
                Some (GitDep (url.[..hashIdx - 1], url.[hashIdx + 1..]))
            else
                Some (GitDep (url, "main"))
    else None

type private Section = Project | Deps | Platform | Other

/// Parse a TOML-subset manifest string.
let parseManifest (src: string) : Result<LLManifest, string> =
    let lines = src.Split('\n')
    let mutable currentSection = Other
    let mutable projectName: string option = None
    let mutable projectVersion = "0.0.0"
    let mutable projectEntry = "src/Main.lll"
    let mutable deps: (string * DepSource) list = []
    let mutable platform: string list = []
    let mutable error: string option = None
    let mutable lineNum = 0

    for rawLine in lines do
        lineNum <- lineNum + 1
        if error.IsNone then
            let line = trimLine rawLine
            if line = "" then
                () // blank / comment line
            elif line.StartsWith("[") then
                // Section header
                let closing = line.IndexOf(']')
                if closing < 0 then
                    error <- Some $"Line {lineNum}: unclosed '[' in section header: {line}"
                else
                    let sectionName = line.[1..closing - 1].Trim()
                    currentSection <-
                        match sectionName with
                        | "project"  -> Project
                        | "deps"     -> Deps
                        | "platform" -> Platform
                        | _          -> Other
            elif line.Contains("=") then
                let eqIdx = line.IndexOf('=')
                let key = line.[..eqIdx - 1].Trim()
                let valueRaw = line.[eqIdx + 1..].Trim()
                match currentSection with
                | Project ->
                    if valueRaw.StartsWith("\"") then
                        match parseQuotedString valueRaw 0 with
                        | Error e -> error <- Some $"Line {lineNum}: {e}"
                        | Ok (v, _) ->
                            match key with
                            | "name"    -> projectName <- Some v
                            | "version" -> projectVersion <- v
                            | "entry"   -> projectEntry <- v
                            | _ -> () // ignore unknown keys
                    else
                        () // ignore non-string values
                | Deps ->
                    // key is a dep name (may be quoted), value is a URL string or inline table
                    let parseKey (k: string) =
                        if k.StartsWith("\"") then
                            match parseQuotedString k 0 with
                            | Ok (v, _) -> Some v
                            | Error _ -> None
                        else Some k
                    let parsedKey = parseKey key
                    let parsedSource = parseDepSource valueRaw
                    match parsedKey, parsedSource with
                    | Some k, Some src -> deps <- deps @ [(k, src)]
                    | _ -> () // skip malformed dep entries
                | Platform ->
                    if key = "use" && valueRaw.StartsWith("[") then
                        match parseStringArray valueRaw 0 with
                        | Error e -> error <- Some $"Line {lineNum}: {e}"
                        | Ok (items, _) -> platform <- items
                    else
                        () // ignore unknown platform keys
                | Other -> () // ignore unknown sections
            else
                () // ignore lines that aren't key=value or section headers

    match error with
    | Some e -> Error e
    | None ->
        match projectName with
        | None -> Error "Missing required key: [project] name"
        | Some name ->
            Ok {
                Name     = name
                Version  = projectVersion
                Entry    = projectEntry
                Deps     = Map.ofList deps
                Platform = platform
            }
