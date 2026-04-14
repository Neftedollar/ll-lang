module Std.Parsec

open LLLang.Prelude

type Maybe<'A> = 'A option

type Result<'A, 'E> =
    | Ok of 'A
    | Err of 'E

type ParsePos =
    | MkParsePos of int64 * int64 * int64

type ParseState =
    | MkParseState of string * char list * ParsePos

type ParseError =
    | MkParseError of string * ParsePos

type ParseStep<'A> =
    | StepOk of 'A * ParseState
    | StepErr of ParseError * ParseState * bool

type Parser<'A> =
    | MkParser of (ParseState -> 'A ParseStep)

let rec posOffset p =
    (match p with | MkParsePos(o, _, _) -> o)

and posLine p =
    (match p with | MkParsePos(_, l, _) -> l)

and posCol p =
    (match p with | MkParsePos(_, _, c) -> c)

and statePos st =
    (match st with | MkParseState(_, _, p) -> p)

and stateChars st =
    (match st with | MkParseState(_, cs, _) -> cs)

and stateOffset st =
    (posOffset (statePos st))

and stateCurrentChar st =
    ((listAt (stateChars st)) (stateOffset st))

and advancePos c p =
    (let o = (posOffset p) in (let l = (posLine p) in (let col = (posCol p) in (if (c = '\n') then (MkParsePos ((o + 1L), (l + 1L), 1L)) else (MkParsePos ((o + 1L), l, (col + 1L)))))))

and stateAdvance c st =
    (match st with | MkParseState(src, cs, p) -> (MkParseState (src, cs, ((advancePos c) p))))

and stateInit src =
    (MkParseState (src, (strChars src), (MkParsePos (0L, 1L, 1L))))

and mkErr msg st =
    (MkParseError (msg, (statePos st)))

and parserRunStep p st =
    (match p with | MkParser(f) -> (f st))

and stateConsumedFrom a b =
    ((stateOffset b) > (stateOffset a))

and isConsumedFrom start finish reported =
    (if reported then true else ((stateConsumedFrom start) finish))

and charInList c xs =
    (match xs with | (x :: rest) -> (if (c = x) then true else ((charInList c) rest)) | _ -> false)

and pushChars cs acc =
    (match cs with | (c :: rest) -> ((pushChars rest) (c :: acc)) | _ -> acc)

and runParser p src =
    (let st0 = (stateInit src) in (match ((parserRunStep p) st0) with | StepOk(v, st1) -> (Ok (v, st1)) | StepErr(e, _, _) -> (Err e)))

and parsePure v =
    (MkParser (fun st -> (StepOk (v, st))))

and parseFail msg =
    (MkParser (fun st -> (StepErr (((mkErr msg) st), st, false))))

and parseMap f p =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(a, st1) -> (StepOk ((f a), st1)) | StepErr(e, stErr, consumed) -> (StepErr (e, stErr, consumed)))))

and parseBind p k =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(a, st1) -> (match ((parserRunStep (k a)) st1) with | StepOk(b, st2) -> (StepOk (b, st2)) | StepErr(e, stErr, consumed2) -> (StepErr (e, stErr, (((isConsumedFrom st0) stErr) consumed2)))) | StepErr(e, stErr, consumed1) -> (StepErr (e, stErr, consumed1)))))

and parseTry p =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(v, st1) -> (StepOk (v, st1)) | StepErr(e, _, _) -> (StepErr (e, st0, false)))))

and parseOrElse p q =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(v, st1) -> (StepOk (v, st1)) | StepErr(e, stErr, consumed) -> (if (((isConsumedFrom st0) stErr) consumed) then (StepErr (e, stErr, true)) else ((parserRunStep q) st0)))))

and parseLabel label p =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(v, st1) -> (StepOk (v, st1)) | StepErr(_, stErr, consumed) -> (StepErr ((MkParseError (label, (statePos stErr))), stErr, consumed)))))

and parseGetState =
    (MkParser (fun st -> (StepOk (st, st))))

and parseSetState next =
    (MkParser (fun ignored -> (StepOk (true, next))))

and parsePeekChar =
    (MkParser (fun st -> (match (stateCurrentChar st) with | Some(c) -> (StepOk (c, st)) | None -> (StepErr (((mkErr "unexpected EOF") st), st, false)))))

and parseAnyChar =
    (MkParser (fun st -> (match (stateCurrentChar st) with | Some(c) -> (StepOk (c, ((stateAdvance c) st))) | None -> (StepErr (((mkErr "unexpected EOF") st), st, false)))))

and parseSatisfy pred expected =
    (MkParser (fun st -> (match (stateCurrentChar st) with | Some(c) -> (if (pred c) then (StepOk (c, ((stateAdvance c) st))) else (StepErr (((mkErr ((strConcat "unexpected char, expected ") expected)) st), st, false))) | None -> (StepErr (((mkErr ((strConcat "unexpected EOF, expected ") expected)) st), st, false)))))

and parseChar ch =
    ((parseSatisfy (fun c -> (c = ch))) ((strConcat "'") ((strConcat (strFromChars [ch])) "'")))

and parseStringFrom expected orig st =
    (match expected with | (ec :: rest) -> (match (stateCurrentChar st) with | Some(c) -> (if (c = ec) then (((parseStringFrom rest) orig) ((stateAdvance c) st)) else (StepErr (((mkErr ((strConcat "expected string ") orig)) st), st, false))) | None -> (StepErr (((mkErr ((strConcat "unexpected EOF, expected string ") orig)) st), st, false))) | _ -> (StepOk (orig, st)))

and parseString s =
    (MkParser (fun st -> (((parseStringFrom (strChars s)) s) st)))

and parseOneOf chars =
    (let charSet = (strChars chars) in ((parseSatisfy (fun c -> ((charInList c) charSet))) ((strConcat "one of ") chars)))

and parseNoneOf chars =
    (let charSet = (strChars chars) in ((parseSatisfy (fun c -> (if ((charInList c) charSet) then false else true))) ((strConcat "none of ") chars)))

and parseEof =
    (MkParser (fun st -> (match (stateCurrentChar st) with | None -> (StepOk (true, st)) | Some(_) -> (StepErr (((mkErr "expected EOF") st), st, false)))))

and parseManyLoop p accRev st =
    (match ((parserRunStep p) st) with | StepOk(v, st1) -> (((parseManyLoop p) (v :: accRev)) st1) | StepErr(e, stErr, consumed) -> (if (((isConsumedFrom st) stErr) consumed) then (StepErr (e, stErr, true)) else (StepOk ((listReverse accRev), st))))

and parseMany p =
    (MkParser (fun st -> (((parseManyLoop p) []) st)))

and parseMany1 p =
    ((parseBind p) (fun x -> ((parseMap (fun xs -> (x :: xs))) (parseMany p))))

and parseOptional p =
    (MkParser (fun st0 -> (match ((parserRunStep p) st0) with | StepOk(v, st1) -> (StepOk ((Some v), st1)) | StepErr(e, stErr, consumed) -> (if (((isConsumedFrom st0) stErr) consumed) then (StepErr (e, stErr, true)) else (StepOk (None, st0))))))

and parseSepBy1 p sep =
    ((parseBind p) (fun x -> ((parseBind (parseMany ((parseBind sep) (fun ignoredSep -> p)))) (fun xs -> (parsePure (x :: xs))))))

and parseSepBy p sep =
    ((parseOrElse (parseTry ((parseSepBy1 p) sep))) (parsePure []))

and parseBetween openP closeP p =
    ((parseBind openP) (fun ignoredOpen -> ((parseBind p) (fun v -> ((parseBind closeP) (fun ignoredClose -> (parsePure v)))))))

and parseWhitespace =
    ((parseSatisfy charIsSpace) "whitespace")

and parseSpaces =
    (parseMany parseWhitespace)

and parseDigit =
    ((parseSatisfy charIsDigit) "digit")

and takeDigits st accRev =
    (match (stateCurrentChar st) with | Some(c) -> (if (charIsDigit c) then ((takeDigits ((stateAdvance c) st)) (c :: accRev)) else ((listReverse accRev), st)) | None -> ((listReverse accRev), st))

and parseInt =
    (MkParser (fun st0 -> (let signPair = (match (stateCurrentChar st0) with | Some('-') -> ("-", ((stateAdvance '-') st0)) | _ -> ("", st0)) in (match signPair with | (sign, st1) -> (let digitsPair = ((takeDigits st1) []) in (match digitsPair with | (digits, st2) -> (if ((listLen digits) = 0L) then (StepErr (((mkErr "expected digit") st1), st1, ((stateConsumedFrom st0) st1))) else (let nStr = ((strConcat sign) (strFromChars digits)) in (match (strToInt nStr) with | Some(n) -> (StepOk (n, st2)) | None -> (StepErr (((mkErr ((strConcat "invalid int literal: ") nStr)) st2), st2, true)))))))))))

and isHexDigit c =
    (if (c >= '0') then (if (c <= '9') then true else (if (c >= 'a') then (if (c <= 'f') then true else (if (c >= 'A') then (if (c <= 'F') then true else false) else false)) else (if (c >= 'A') then (if (c <= 'F') then true else false) else false))) else (if (c >= 'a') then (if (c <= 'f') then true else (if (c >= 'A') then (if (c <= 'F') then true else false) else false)) else (if (c >= 'A') then (if (c <= 'F') then true else false) else false)))

and hexVal c =
    (if (c >= '0') then (if (c <= '9') then ((charToInt c) - (charToInt '0')) else (if (c >= 'a') then (if (c <= 'f') then (10L + ((charToInt c) - (charToInt 'a'))) else (if (c >= 'A') then (if (c <= 'F') then (10L + ((charToInt c) - (charToInt 'A'))) else (0L - 1L)) else (0L - 1L))) else (if (c >= 'A') then (if (c <= 'F') then (10L + ((charToInt c) - (charToInt 'A'))) else (0L - 1L)) else (0L - 1L)))) else (if (c >= 'a') then (if (c <= 'f') then (10L + ((charToInt c) - (charToInt 'a'))) else (if (c >= 'A') then (if (c <= 'F') then (10L + ((charToInt c) - (charToInt 'A'))) else (0L - 1L)) else (0L - 1L))) else (if (c >= 'A') then (if (c <= 'F') then (10L + ((charToInt c) - (charToInt 'A'))) else (0L - 1L)) else (0L - 1L))))

and readHex4 st acc count =
    (if (count = 0L) then (StepOk (acc, st)) else (match (stateCurrentChar st) with | Some(c) -> (if (isHexDigit c) then (let nextAcc = ((acc * 16L) + (hexVal c)) in (((readHex4 ((stateAdvance c) st)) nextAcc) (count - 1L))) else (StepErr (((mkErr "invalid hex digit in \\u escape") st), st, false))) | None -> (StepErr (((mkErr "incomplete \\u escape") st), st, false))))

and expectChar st want msg =
    (match (stateCurrentChar st) with | Some(c) -> (if (c = want) then (StepOk (c, ((stateAdvance c) st))) else (StepErr (((mkErr msg) st), st, false))) | None -> (StepErr (((mkErr msg) st), st, false)))

and parseUnicodeEscapeChars st =
    (match (((readHex4 st) 0L) 4L) with | StepErr(e, stErr, consumed) -> (StepErr (e, stErr, consumed)) | StepOk(hi, rest) -> (if (hi >= 55296L) then (if (hi <= 56319L) then (match (((expectChar rest) '\\') "expected second \\u escape for surrogate pair") with | StepErr(e, stErr, consumed) -> (StepErr (e, stErr, consumed)) | StepOk(_, rest2) -> (match (((expectChar rest2) 'u') "expected second \\u escape for surrogate pair") with | StepErr(e2, stErr2, consumed2) -> (StepErr (e2, stErr2, consumed2)) | StepOk(_, rest3) -> (match (((readHex4 rest3) 0L) 4L) with | StepErr(e3, stErr3, consumed3) -> (StepErr (e3, stErr3, consumed3)) | StepOk(lo, rest4) -> (if (lo >= 56320L) then (if (lo <= 57343L) then (StepOk ([(intToChar hi); (intToChar lo)], rest4)) else (StepErr (((mkErr "invalid low surrogate in \\u pair") rest4), rest4, true))) else (StepErr (((mkErr "expected low surrogate after high surrogate") rest4), rest4, true)))))) else (if (hi <= 57343L) then (StepErr (((mkErr "unexpected low surrogate without leading high surrogate") rest), rest, true)) else (StepOk ([(intToChar hi)], rest)))) else (StepOk ([(intToChar hi)], rest))))

and parseEscapeChars st =
    (match (stateCurrentChar st) with | Some('"') -> (StepOk (['"'], ((stateAdvance '"') st))) | Some('\\') -> (StepOk (['\\'], ((stateAdvance '\\') st))) | Some('/') -> (StepOk (['/'], ((stateAdvance '/') st))) | Some('b') -> (StepOk ([(intToChar 8L)], ((stateAdvance 'b') st))) | Some('f') -> (StepOk ([(intToChar 12L)], ((stateAdvance 'f') st))) | Some('n') -> (StepOk (['\n'], ((stateAdvance 'n') st))) | Some('r') -> (StepOk (['\r'], ((stateAdvance 'r') st))) | Some('t') -> (StepOk (['\t'], ((stateAdvance 't') st))) | Some('u') -> (parseUnicodeEscapeChars ((stateAdvance 'u') st)) | Some(_) -> (StepErr (((mkErr "invalid escape sequence in string") st), st, false)) | None -> (StepErr (((mkErr "unterminated string escape") st), st, false)))

and scanQuotedChars st accRev =
    (match (stateCurrentChar st) with | Some('"') -> (StepOk ((strFromChars (listReverse accRev)), ((stateAdvance '"') st))) | Some('\\') -> (match (parseEscapeChars ((stateAdvance '\\') st)) with | StepErr(e, stErr, consumed) -> (StepErr (e, stErr, consumed)) | StepOk(outChars, st2) -> ((scanQuotedChars st2) ((pushChars outChars) accRev))) | Some(c) -> (if ((charToInt c) < 32L) then (StepErr (((mkErr "control character in string literal") st), st, true)) else ((scanQuotedChars ((stateAdvance c) st)) (c :: accRev))) | None -> (StepErr (((mkErr "unterminated string literal") st), st, true)))

and parseQuotedString =
    (MkParser (fun st0 -> (match (stateCurrentChar st0) with | Some('"') -> ((scanQuotedChars ((stateAdvance '"') st0)) []) | _ -> (StepErr (((mkErr "expected quoted string") st0), st0, false)))))