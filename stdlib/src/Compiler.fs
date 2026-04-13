module Std.Compiler

open LLLang.Prelude
open Std.Maybe
open Std.Lexer
open Std.Parser
open Std.Render
open Std.Elaborator
open Std.Codegen

let rec showErrors errs =
    (((listFold (fun acc e -> (let msg = (errMsg e) in (if ((strLen acc) = 0L) then msg else ((strConcat acc) ((strConcat "\n") msg)))))) "") errs)

and encodeJsonEscape c =
    (match c with | '\n' -> "\\n" | '\t' -> "\\t" | '\r' -> "\\r" | '\\' -> "\\\\" | '"' -> "\\\"" | _ -> (strFromChars [c]))

and escapeJson s =
    (((listFold (fun acc c -> ((strConcat acc) (encodeJsonEscape c)))) "") (strChars s))

and jsonStr s =
    ((strConcat "\"") ((strConcat (escapeJson s)) "\""))

and firstError errs =
    (match errs with | (err :: _) -> (errMsg err) | [] -> "")

and secondaryErrorCount errs =
    (match errs with | (_ :: rest) -> (listLen rest) | [] -> 0L)

and declPrimaryName d =
    (match d with | DFn(name, _, _, _) -> name | DType(name, _, _) -> name | DLet(name, _) -> name | DImport(segs) -> (((listFold (fun acc seg -> (if ((strLen acc) = 0L) then seg else ((strConcat acc) ((strConcat ".") seg))))) "") segs) | DExport(inner) -> (declPrimaryName inner))

and declKindTag d =
    (match d with | DFn(_, _, _, _) -> "fn" | DType(_, _, _) -> "type" | DLet(_, _) -> "let" | DImport(_) -> "import" | DExport(inner) -> (declKindTag inner))

and findSymbolKind name decls =
    (match decls with | [] -> "" | (d :: rest) -> (if ((declPrimaryName d) = name) then (declKindTag d) else ((findSymbolKind name) rest)))

and hasLexErrors tokens =
    (match tokens with | (TError(_) :: _) -> true | (_ :: rest) -> (hasLexErrors rest) | [] -> false)

and firstLexError tokens =
    (match tokens with | (TError(c) :: _) -> ((strConcat "Unexpected char: ") (strFromChars [c])) | (_ :: rest) -> (firstLexError rest) | [] -> "")

and countAllTokens tokens =
    (match tokens with | (_ :: rest) -> (1L + (countAllTokens rest)) | [] -> 0L)

and countCodeTokens tokens =
    (match tokens with | [] -> 0L | (Newline :: rest) -> (countCodeTokens rest) | (Indent :: rest) -> (countCodeTokens rest) | (Dedent :: rest) -> (countCodeTokens rest) | (Eof :: rest) -> (countCodeTokens rest) | (_ :: rest) -> (1L + (countCodeTokens rest)))

and checkCompact src =
    (let tokens = (tokenize src) in (if (hasLexErrors tokens) then (let p1 = "{\"ok\":false,\"stage\":\"lexer\",\"primary_error\":" in (let p2 = ((strConcat p1) (jsonStr (firstLexError tokens))) in ((strConcat p2) ",\"secondary_count\":0}"))) else (let ast = (parseModule tokens) in (let errs = (elaborate ast) in (match errs with | [] -> "{\"ok\":true,\"stage\":\"ok\",\"primary_error\":\"\",\"secondary_count\":0}" | _ -> (let p1 = "{\"ok\":false,\"stage\":\"elaborate\",\"primary_error\":" in (let p2 = ((strConcat p1) (jsonStr (firstError errs))) in (let p3 = ((strConcat p2) ",\"secondary_count\":") in ((strConcat p3) ((strConcat (intToStr (secondaryErrorCount errs))) "}"))))))))))

and nextBlocker src =
    (let tokens = (tokenize src) in (if (hasLexErrors tokens) then (let p1 = "{\"ok\":false,\"blocker\":\"lexer\",\"stage\":\"lexer\",\"primary_error\":" in (let p2 = ((strConcat p1) (jsonStr (firstLexError tokens))) in ((strConcat p2) ",\"secondary_count\":0}"))) else (let ast = (parseModule tokens) in (let errs = (elaborate ast) in (match errs with | [] -> "{\"ok\":true,\"blocker\":\"ok\",\"stage\":\"ok\",\"primary_error\":\"\",\"secondary_count\":0}" | _ -> (let p1 = "{\"ok\":false,\"blocker\":\"elaborate\",\"stage\":\"elaborate\",\"primary_error\":" in (let p2 = ((strConcat p1) (jsonStr (firstError errs))) in (let p3 = ((strConcat p2) ",\"secondary_count\":") in ((strConcat p3) ((strConcat (intToStr (secondaryErrorCount errs))) "}"))))))))))

and lookupSymbol src name =
    (let tokens = (tokenize src) in (if (hasLexErrors tokens) then (let p1 = "{\"ok\":false,\"stage\":\"lexer\",\"symbol\":" in (let p2 = ((strConcat p1) (jsonStr name)) in (let p3 = ((strConcat p2) ",\"primary_error\":") in (let p4 = ((strConcat p3) (jsonStr (firstLexError tokens))) in ((strConcat p4) ",\"secondary_count\":0}"))))) else (let ast = (parseModule tokens) in (match ast with | MkModule(_, decls) -> (let kind = ((findSymbolKind name) decls) in (if ((strLen kind) = 0L) then (let p1 = "{\"ok\":false,\"stage\":\"lookup\",\"symbol\":" in (let p2 = ((strConcat p1) (jsonStr name)) in (let p3 = ((strConcat p2) ",\"primary_error\":") in (let p4 = ((strConcat p3) (jsonStr ((strConcat "Symbol not found: ") name))) in ((strConcat p4) ",\"secondary_count\":0}"))))) else (let p1 = "{\"ok\":true,\"stage\":\"lookup\",\"symbol\":" in (let p2 = ((strConcat p1) (jsonStr name)) in (let p3 = ((strConcat p2) ",\"kind\":") in (let p4 = ((strConcat p3) (jsonStr kind)) in ((strConcat p4) "}")))))))))))

and compileCompact src =
    (let tokens = (tokenize src) in (if (hasLexErrors tokens) then (checkCompact src) else (let ast = (parseModule tokens) in (let errs = (elaborate ast) in (match errs with | [] -> (let out = (emitModule ast) in (let p1 = "{\"ok\":true,\"stage\":\"codegen\",\"output_bytes\":" in (let p2 = ((strConcat p1) (intToStr (strLen out))) in ((strConcat p2) "}")))) | _ -> (checkCompact src))))))

and renderCompact src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (renderModule ast)))

and tokenEstimate src =
    (let tokens = (tokenize src) in (let p1 = "{\"ok\":true,\"source_bytes\":" in (let p2 = ((strConcat p1) (intToStr (strLen src))) in (let p3 = ((strConcat p2) ",\"lexer_tokens\":") in (let p4 = ((strConcat p3) (intToStr (countAllTokens tokens))) in (let p5 = ((strConcat p4) ",\"non_layout_tokens\":") in ((strConcat p5) ((strConcat (intToStr (countCodeTokens tokens))) "}"))))))))

and compile src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let errors = (elaborate ast) in (match errors with | [] -> (emitModule ast) | _ -> (showErrors errors)))))

and compilerCheckContains label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  missing:  ") ((strConcat needle) ((strConcat "\n  in output:\n") got)))))))

and checkHasError label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  expected error containing: ") ((strConcat needle) ((strConcat "\n  got: ") got)))))))

[<EntryPoint>]
let main (argv: string[]) =
    (let src1 = "module Test.Hello\nadd(a Int)(b Int) = a + b\n" in (let out1 = (compile src1) in (let _ = (((compilerCheckContains "1 module header") out1) "module Test.Hello") in (let _ = (((compilerCheckContains "1 prelude present") out1) "ll-lang stdlib prelude") in (let _ = (((compilerCheckContains "1 add function") out1) "let rec add") in (let src2 = "module Test.Colors\nColor = Red | Blue | Green\ndescribe(c Color) =\n  match c\n    | Red -> \"red\"\n    | Blue -> \"blue\"\n    | Green -> \"green\"\n" in (let out2 = (compile src2) in (let _ = (((compilerCheckContains "2 module colors") out2) "module Test.Colors") in (let _ = (((compilerCheckContains "2 type Color") out2) "type Color") in (let _ = (((compilerCheckContains "2 ctor Red") out2) "| Red") in (let _ = (((compilerCheckContains "2 describe fn") out2) "let rec describe") in (let _ = (((compilerCheckContains "2 match expr") out2) "match") in (let src3 = "module Test.Bad\nbroken(x Int) = y\n" in (let out3 = (compile src3) in (let _ = (((checkHasError "3 unbound var error") out3) "Unbound variable: y") in (let src4 = "module Test.Const\nlet answer = 42\n" in (let out4 = (compile src4) in (let _ = (((compilerCheckContains "4 module const") out4) "module Test.Const") in (let _ = (((compilerCheckContains "4 let binding") out4) "let answer") in (let src5 = "module Test.IfElse\nabs(n Int) = if n < 0\n  0 - n\nelse n\n" in (let out5 = (compile src5) in (let _ = (((compilerCheckContains "5 abs function") out5) "let rec abs") in (let _ = (((compilerCheckContains "5 if expression") out5) "if") in (let chk6 = (checkCompact src3) in (let _ = (((compilerCheckContains "6 compact stage") chk6) "\"stage\":\"elaborate\"") in (let _ = (((compilerCheckContains "6 compact primary error") chk6) "\"primary_error\":\"Unbound variable: y\"") in (let src7 = "module Test.Render\nadd(a Int)(b Int) = a + b\n" in (let out7 = (renderCompact src7) in (let _ = (((compilerCheckContains "7 render module") out7) "module Test.Render") in (let _ = (((compilerCheckContains "7 render fn") out7) "add(a Int)(b Int) = (a + b)") in (let tok8 = (tokenEstimate src1) in (let _ = (((compilerCheckContains "8 tokens has bytes") tok8) "\"source_bytes\":") in (let _ = (((compilerCheckContains "8 tokens has non layout") tok8) "\"non_layout_tokens\":") in 0L)))))))))))))))))))))))))))))))))
    0