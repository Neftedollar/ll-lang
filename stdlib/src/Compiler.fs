module Std.Compiler

open LLLang.Prelude
open Std.Maybe
open Std.Lexer
open Std.Parser
open Std.Elaborator
open Std.Codegen

let rec showErrors errs =
    (((listFold (fun acc e -> (let msg = (errMsg e) in (if ((strLen acc) = 0L) then msg else ((strConcat acc) ((strConcat "\n") msg)))))) "") errs)

and compile src =
    (let tokens = (tokenize src) in (let ast = (parseModule tokens) in (let errors = (elaborate ast) in (if (listIsEmpty errors) then (emitModule ast) else (showErrors errors)))))

and compilerCheckContains label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  missing:  ") ((strConcat needle) ((strConcat "\n  in output:\n") got)))))))

and checkHasError label got needle =
    (if ((strContains needle) got) then (printfn ((strConcat "OK ") label)) else (printfn ((strConcat "FAIL ") ((strConcat label) ((strConcat "\n  expected error containing: ") ((strConcat needle) ((strConcat "\n  got: ") got)))))))