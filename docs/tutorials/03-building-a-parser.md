# Tutorial 03: Building a Parser

A JSON value parser in ll-lang — real-world functional code in under 80 lines. This tutorial shows how ll-lang's ADTs, pattern matching, and recursion compose cleanly for parsing tasks. The stdlib's `Toml.lll` (292 lines, parsing TOML config files) is the same pattern at larger scale.

## The value type

JSON has six value kinds. One ADT captures all of them:

```lll
module Json

JsonValue =
  | JNull
  | JBool Bool
  | JNum Float
  | JStr Str
  | JArr (List[JsonValue])
  | JObj (List[(Str, JsonValue)])
```

In TypeScript this would require a discriminated union with explicit `tag` fields on every variant — about 4× the tokens. In ll-lang, the constructors are the tags.

## Token representation

```lll
Token =
  | TNull
  | TTrue
  | TFalse
  | TNum Str
  | TStr Str
  | TLBrace
  | TRBrace
  | TLBracket
  | TRBracket
  | TColon
  | TComma
  | TEOF
```

## Lexer

A simple character-at-a-time lexer. `strChars` splits a `Str` into `List[Char]`.

```lll
skipWhitespace(cs List[Char]) =
  match cs
    | [] -> []
    | c :: rest ->
      if c == ' ' || c == '\n' || c == '\r' || c == '\t'
        skipWhitespace rest
      else cs

scanString(cs List[Char])(acc List[Char]) =
  match cs
    | [] -> (strFromChars (listReverse acc), [])
    | '"' :: rest -> (strFromChars (listReverse acc), rest)
    | c :: rest   -> scanString rest (c :: acc)

scanDigits(cs List[Char])(acc List[Char]) =
  match cs
    | [] -> (strFromChars (listReverse acc), [])
    | c :: rest ->
      if charIsDigit c || c == '.' || c == '-'
        scanDigits rest (c :: acc)
      else (strFromChars (listReverse acc), cs)

nextToken(cs List[Char]) =
  trimmed = skipWhitespace cs
  match trimmed
    | []            -> (TEOF, [])
    | '"' :: rest   ->
      let (s, rem) = scanString rest []
      (TStr s, rem)
    | '{' :: rest   -> (TLBrace, rest)
    | '}' :: rest   -> (TRBrace, rest)
    | '[' :: rest   -> (TLBracket, rest)
    | ']' :: rest   -> (TRBracket, rest)
    | ':' :: rest   -> (TColon, rest)
    | ',' :: rest   -> (TComma, rest)
    | 'n' :: 'u' :: 'l' :: 'l' :: rest -> (TNull, rest)
    | 't' :: 'r' :: 'u' :: 'e' :: rest -> (TTrue, rest)
    | 'f' :: 'a' :: 'l' :: 's' :: 'e' :: rest -> (TFalse, rest)
    | c :: _ ->
      if charIsDigit c || c == '-'
        let (n, rem) = scanDigits trimmed []
        (TNum n, rem)
      else (TEOF, [])

tokenize(src Str) =
  tokenizeChars (strChars src) []

tokenizeChars(cs List[Char])(acc List[Token]) =
  let (tok, rest) = nextToken cs
  match tok
    | TEOF -> listReverse (TEOF :: acc)
    | _    -> tokenizeChars rest (tok :: acc)
```

## Parser

A recursive-descent parser. The `ParseResult A` type threads the remaining token list through each call.

```lll
ParseResult A = ParseOk A (List[Token]) | ParseErr Str

parseValue(tokens List[Token]) =
  match tokens
    | TNull      :: rest -> ParseOk JNull rest
    | TTrue      :: rest -> ParseOk (JBool true) rest
    | TFalse     :: rest -> ParseOk (JBool false) rest
    | TNum n     :: rest ->
      match strToFloat n
        | Some f -> ParseOk (JNum f) rest
        | None   -> ParseErr (strConcat "bad number: " n)
    | TStr s     :: rest -> ParseOk (JStr s) rest
    | TLBracket  :: rest -> parseArray rest []
    | TLBrace    :: rest -> parseObject rest []
    | _                  -> ParseErr "unexpected token"

parseArray(tokens List[Token])(acc List[JsonValue]) =
  match tokens
    | TRBracket :: rest -> ParseOk (JArr (listReverse acc)) rest
    | _ ->
      match parseValue tokens
        | ParseErr e -> ParseErr e
        | ParseOk v rest2 ->
          match rest2
            | TComma      :: rest3 -> parseArray rest3 (v :: acc)
            | TRBracket   :: rest3 -> ParseOk (JArr (listReverse (v :: acc))) rest3
            | _                    -> ParseErr "expected , or ]"

parseObject(tokens List[Token])(acc List[(Str, JsonValue)]) =
  match tokens
    | TRBrace :: rest -> ParseOk (JObj (listReverse acc)) rest
    | TStr k :: TColon :: rest ->
      match parseValue rest
        | ParseErr e -> ParseErr e
        | ParseOk v rest2 ->
          match rest2
            | TComma    :: rest3 -> parseObject rest3 ((k, v) :: acc)
            | TRBrace   :: rest3 -> ParseOk (JObj (listReverse ((k, v) :: acc))) rest3
            | _                  -> ParseErr "expected , or }"
    | _ -> ParseErr "expected string key"

parse(src Str) =
  tokens = tokenize src
  match parseValue tokens
    | ParseOk v _ -> Some v
    | ParseErr _  -> None
```

## Usage

```lll
main() =
  result = parse "{\"name\": \"alice\", \"age\": 30}"
  match result
    | None   -> printfn "parse error"
    | Some v ->
      match v
        | JObj pairs ->
          _ = printfn (strConcat "Parsed " (intToStr (listLen pairs)) " keys")
          0
        | _ ->
          _ = printfn "expected object"
          0
```

## Why this is shorter than the alternatives

| Language | Equivalent parser LOC | Key reason |
|----------|----------------------|------------|
| ll-lang | ~80 | ADT constructors are the tokens; match is exhaustive |
| TypeScript | ~180 | Discriminated unions need explicit `tag` fields; no exhaustiveness by default |
| Python | ~150 | `isinstance` chains; no ADTs |
| Java | ~250 | Sealed interfaces + `instanceof` patterns (Java 21); verbose class declarations |

The `JsonValue` ADT declaration is 8 lines in ll-lang. In Java 21 it is approximately 40 lines of sealed interface + record declarations.

## Connection to stdlib

`stdlib/src/Toml.lll` follows exactly this structure:
- ADT for `Section` (the parser state variant)
- `ParseState` record threading state through line processing
- Recursive descent via pattern matching on `List[Str]`
- No mutable state, no exceptions, `Maybe` for failure

Read it as a larger worked example of the same pattern.

## Next steps

- [04-multi-target.md](04-multi-target.md) — compile this code to TypeScript, Python, and Java
- `stdlib/src/Toml.lll` — production TOML parser, same idioms
- `stdlib/src/Parser.lll` — the full ll-lang parser (802 LOC)
