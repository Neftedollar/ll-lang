module Std.Json

open LLLang.Prelude
open Std.Parsec

type JsonField =
    | JField of string * JsonValue
and JsonValue =
    | JNull
    | JBool of bool
    | JNum of string
    | JStr of string
    | JArr of JsonValue list
    | JObj of JsonField list

type Token =
    | TNull
    | TTrue
    | TFalse
    | TNum of string
    | TStr of string
    | TLBrace
    | TRBrace
    | TLBracket
    | TRBracket
    | TColon
    | TComma
    | TEOF

type ParseResult<'A> =
    | ParseOk of 'A * Token list
    | ParseErr of string

let rec kindOf v =
    (match v with | JNull -> "null" | JBool(_) -> "bool" | JNum(_) -> "number" | JStr(_) -> "string" | JArr(_) -> "array" | JObj(_) -> "object")

and maybeStr m fallback =
    (match m with | Some(s) -> s | None -> fallback)

and signStr m =
    (match m with | Some(s) -> s | None -> "")

and posToStr pos =
    (match pos with | MkParsePos(_, l, c) -> ((strConcat (intToStr l)) ((strConcat ":") (intToStr c))))

and showParseError e =
    (match e with | MkParseError(msg, pos) -> ((strConcat msg) ((strConcat " at ") (posToStr pos))))

and jsonLexemeStr p =
    ((parseBind parseSpaces) (fun ignoredLeftWs -> ((parseBind p) (fun v -> ((parseBind parseSpaces) (fun ignoredRightWs -> (parsePure v)))))))

and jsonLexemeChar p =
    ((parseBind parseSpaces) (fun ignoredLeftWs -> ((parseBind p) (fun v -> ((parseBind parseSpaces) (fun ignoredRightWs -> (parsePure v)))))))

and jsonSym ch =
    (jsonLexemeChar (parseChar ch))

and parseDigit19 =
    (parseOneOf "123456789")

and parseIntPartStr =
    ((parseOrElse (parseTry ((parseBind (parseChar '0')) (fun ignoredZero -> ((parseBind (parseOptional parsePeekChar)) (fun mc -> (match mc with | Some(c) -> (if (charIsDigit c) then (parseFail "leading zero in number") else (parsePure "0")) | None -> (parsePure "0")))))))) ((parseBind parseDigit19) (fun d1 -> ((parseMap (fun tailDigits -> (strFromChars (d1 :: tailDigits)))) (parseMany parseDigit)))))

and parseFracPartReqStr =
    ((parseBind (parseChar '.')) (fun ignoredDot -> ((parseMap (fun digits -> ((strConcat ".") (strFromChars digits)))) (parseMany1 parseDigit))))

and parseFracPartStr =
    ((parseOrElse (parseTry parseFracPartReqStr)) (parsePure ""))

and parseSignPartStr =
    ((parseOrElse ((parseMap (fun c -> (strFromChars [c]))) (parseOneOf "+-"))) (parsePure ""))

and parseExpPartReqStr =
    ((parseBind (parseOneOf "eE")) (fun eChar -> ((parseBind parseSignPartStr) (fun signPart -> ((parseMap (fun digits -> ((strConcat (strFromChars [eChar])) ((strConcat signPart) (strFromChars digits))))) (parseMany1 parseDigit))))))

and parseExpPartStr =
    ((parseOrElse (parseTry parseExpPartReqStr)) (parsePure ""))

and parseMinusPartStr =
    ((parseOrElse ((parseMap (fun c -> (strFromChars [c]))) (parseOneOf "-"))) (parsePure ""))

and buildJsonNumberExp minusPart intPart fracPart expPart =
    (parsePure ((strConcat minusPart) ((strConcat intPart) ((strConcat fracPart) expPart))))

and buildJsonNumberFrac minusPart intPart fracPart =
    ((parseBind parseExpPartStr) (fun expPart -> ((((buildJsonNumberExp minusPart) intPart) fracPart) expPart)))

and buildJsonNumberInt minusPart intPart =
    ((parseBind parseFracPartStr) (fun fracPart -> (((buildJsonNumberFrac minusPart) intPart) fracPart)))

and buildJsonNumberMinus minusPart =
    ((parseBind parseIntPartStr) (fun intPart -> ((buildJsonNumberInt minusPart) intPart)))

and parseJsonNumberStr =
    (jsonLexemeStr ((parseBind parseMinusPartStr) (fun minusPart -> (buildJsonNumberMinus minusPart))))

and parseJsonNumber =
    ((parseMap (fun n -> (JNum n))) parseJsonNumberStr)

and parseJsonNull =
    ((parseMap (fun ignoredNull -> JNull)) (jsonLexemeStr (parseString "null")))

and parseJsonBool =
    ((parseBind parseSpaces) (fun ignoredLeftWs -> ((parseBind (parseOneOf "tf")) (fun head -> (if (head = 't') then ((parseBind (parseString "rue")) (fun ignoredTrueTail -> ((parseBind parseSpaces) (fun ignoredRightWs -> (parsePure (JBool true)))))) else ((parseBind (parseString "alse")) (fun ignoredFalseTail -> ((parseBind parseSpaces) (fun ignoredRightWs -> (parsePure (JBool false)))))))))))

and parseJsonString =
    ((parseMap (fun s -> (JStr s))) (jsonLexemeStr parseQuotedString))

and parseJsonField =
    ((parseBind (jsonLexemeStr parseQuotedString)) (fun k -> ((parseBind (jsonSym ':')) (fun ignoredColon -> ((parseBind parseJsonValue) (fun v -> (parsePure (JField (k, v)))))))))

and parseJsonArray =
    ((parseBind (jsonSym '[')) (fun ignoredLBrack -> ((parseBind ((parseSepBy parseJsonValue) (jsonSym ','))) (fun items -> ((parseBind (jsonSym ']')) (fun ignoredRBrack -> (parsePure (JArr items))))))))

and parseJsonObject =
    ((parseBind (jsonSym '{')) (fun ignoredLBrace -> ((parseBind ((parseSepBy parseJsonField) (jsonSym ','))) (fun fields -> ((parseBind (jsonSym '}')) (fun ignoredRBrace -> (parsePure (JObj fields))))))))

and parseJsonValue =
    ((parseOrElse (parseTry parseJsonNull)) ((parseOrElse (parseTry parseJsonBool)) ((parseOrElse (parseTry parseJsonNumber)) ((parseOrElse (parseTry parseJsonString)) ((parseOrElse (parseTry parseJsonArray)) parseJsonObject)))))

and parseJsonDocument =
    ((parseBind parseSpaces) (fun ignoredDocLeftWs -> ((parseBind parseJsonValue) (fun v -> ((parseBind parseSpaces) (fun ignoredDocRightWs -> ((parseBind parseEof) (fun ignoredEof -> (parsePure v)))))))))

and parseJson src =
    (match ((runParser parseJsonDocument) src) with | Ok((v, _)) -> (ParseOk (v, [])) | Err(e) -> (ParseErr (showParseError e)))

and parse src =
    (parseJson src)

and eqJsonValueList xs ys =
    (match xs with | (x :: restX) -> (match ys with | (y :: restY) -> (if ((eqJsonValue x) y) then ((eqJsonValueList restX) restY) else false) | _ -> false) | _ -> (match ys with | [] -> true | _ -> false))

and eqJsonField f1 f2 =
    (match f1 with | JField(k1, v1) -> (match f2 with | JField(k2, v2) -> (if (k1 = k2) then ((eqJsonValue v1) v2) else false)))

and eqJsonFieldList xs ys =
    (match xs with | (x :: restX) -> (match ys with | (y :: restY) -> (if ((eqJsonField x) y) then ((eqJsonFieldList restX) restY) else false) | _ -> false) | _ -> (match ys with | [] -> true | _ -> false))

and eqJsonValue v1 v2 =
    (match v1 with | JNull -> (match v2 with | JNull -> true | _ -> false) | JBool(b1) -> (match v2 with | JBool(b2) -> (b1 = b2) | _ -> false) | JNum(n1) -> (match v2 with | JNum(n2) -> (n1 = n2) | _ -> false) | JStr(s1) -> (match v2 with | JStr(s2) -> (s1 = s2) | _ -> false) | JArr(xs1) -> (match v2 with | JArr(xs2) -> ((eqJsonValueList xs1) xs2) | _ -> false) | JObj(fs1) -> (match v2 with | JObj(fs2) -> ((eqJsonFieldList fs1) fs2) | _ -> false))

and escapeChar c =
    (if (c = '"') then "\\\"" else (if (c = '\\') then "\\\\" else (if (c = '\n') then "\\n" else (if (c = '\r') then "\\r" else (if (c = '\t') then "\\t" else (if (c = (intToChar 8L)) then "\\b" else (if (c = (intToChar 12L)) then "\\f" else (strFromChars [c]))))))))

and escapeChars cs acc =
    (match cs with | (c :: rest) -> ((escapeChars rest) ((strConcat acc) (escapeChar c))) | _ -> acc)

and renderString s =
    ((strConcat "\"") ((strConcat ((escapeChars (strChars s)) "")) "\""))

and renderField f =
    (match f with | JField(k, v) -> ((strConcat (renderString k)) ((strConcat ":") (renderJson v))))

and renderArrayTail xs =
    (match xs with | (x :: rest) -> ((strConcat ",") ((strConcat (renderJson x)) (renderArrayTail rest))) | _ -> "")

and renderArray xs =
    (match xs with | (x :: rest) -> ((strConcat "[") ((strConcat (renderJson x)) ((strConcat (renderArrayTail rest)) "]"))) | _ -> "[]")

and renderObjectTail fs =
    (match fs with | (f :: rest) -> ((strConcat ",") ((strConcat (renderField f)) (renderObjectTail rest))) | _ -> "")

and renderObject fs =
    (match fs with | (f :: rest) -> ((strConcat "{") ((strConcat (renderField f)) ((strConcat (renderObjectTail rest)) "}"))) | _ -> "{}")

and renderJson v =
    (match v with | JNull -> "null" | JBool(b) -> (if b then "true" else "false") | JNum(n) -> n | JStr(s) -> (renderString s) | JArr(xs) -> (renderArray xs) | JObj(fs) -> (renderObject fs))

and stringify v =
    (renderJson v)

and equalJson v1 v2 =
    ((eqJsonValue v1) v2)

and mkOk name =
    ((strConcat "OK ") name)

and mkFail name msg =
    ((strConcat ((strConcat "FAIL ") name)) ((strConcat " ") msg))

and runOkCase name src wantKind =
    (match (parseJson src) with | ParseErr(e) -> ((mkFail name) ((strConcat "parse-error:") e)) | ParseOk(v, _) -> (let got = (kindOf v) in (if (got = wantKind) then (mkOk name) else ((mkFail name) ((strConcat "kind-mismatch:") got)))))

and runErrCase name src =
    (match (parseJson src) with | ParseErr(_) -> (mkOk name) | ParseOk(v, _) -> ((mkFail name) ((strConcat "expected-error-got:") (kindOf v))))

and runRoundtripCase name src =
    (match (parseJson src) with | ParseErr(e) -> ((mkFail name) ((strConcat "parse-error:") e)) | ParseOk(v, _) -> (let rendered = (renderJson v) in (match (parseJson rendered) with | ParseErr(e2) -> ((mkFail name) ((strConcat "reparse-error:") e2)) | ParseOk(v2, _) -> (if ((eqJsonValue v) v2) then (mkOk name) else ((mkFail name) "roundtrip-ast-mismatch")))))