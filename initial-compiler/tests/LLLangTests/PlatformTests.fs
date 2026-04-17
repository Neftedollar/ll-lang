module LLLang.Tests.PlatformTests

open Xunit
open LLLang.Platform

[<Fact>]
let ``Platform: target aliases parse to canonical targets`` () =
    Assert.Equal(Some FSharp, tryParseTarget "fs")
    Assert.Equal(Some FSharp, tryParseTarget "Platform.FSharp.SDK")
    Assert.Equal(Some TypeScript, tryParseTarget "typescript")
    Assert.Equal(Some Python, tryParseTarget "py")
    Assert.Equal(Some Java, tryParseTarget "jvm")
    Assert.Equal(Some CSharp, tryParseTarget "Platform.CSharp.SDK")
    Assert.Equal(Some CSharp, tryParseTarget "cs")
    Assert.Equal(Some LLVM, tryParseTarget "Platform.LLVM.SDK")
    Assert.Equal(Some LLVM, tryParseTarget "llvm")

[<Fact>]
let ``Platform: normalizePlatforms canonicalizes and deduplicates`` () =
    let normalized = normalizePlatforms ["fs"; "typescript"; "py"; "python"; "cs"; "llvm"; "fsharp"]
    Assert.Equal<string list>(["fsharp"; "typescript"; "python"; "csharp"; "llvm"], normalized)

[<Fact>]
let ``Platform: target metadata exposes expected output extensions`` () =
    Assert.Equal(".fs", targetOutputExt FSharp)
    Assert.Equal(".ts", targetOutputExt TypeScript)
    Assert.Equal(".py", targetOutputExt Python)
    Assert.Equal(".java", targetOutputExt Java)
    Assert.Equal(".cs", targetOutputExt CSharp)
    Assert.Equal(".ll", targetOutputExt LLVM)

[<Fact>]
let ``Platform SDK: build and run commands are available from metadata`` () =
    Assert.Equal(Some "dotnet build {project_file_q} -c Release", tryGetBuildCompileCommand FSharp)
    Assert.Equal(Some "dotnet run --project {project_file_q}", tryGetBuildRunCommand FSharp)
    Assert.Equal(Some "npx tsc {main_file_q} --target es2022 --module esnext", tryGetBuildCompileCommand TypeScript)
    Assert.Equal(Some "npx tsc {main_file_q} --target es2022 --module esnext && node {main_js_file_q}", tryGetBuildRunCommand TypeScript)
    Assert.Equal(Some "if command -v python >/dev/null 2>&1; then python -m py_compile {main_file_q}; else python3 -m py_compile {main_file_q}; fi", tryGetBuildCompileCommand Python)
    Assert.Equal(Some "if command -v python >/dev/null 2>&1; then python {main_file_q}; else python3 {main_file_q}; fi", tryGetBuildRunCommand Python)
    Assert.Equal(Some "javac {java_source_file_q}", tryGetBuildCompileCommand Java)
    Assert.Equal(Some "javac {java_source_file_q} && java {java_class_name_q}", tryGetBuildRunCommand Java)
    Assert.Equal(Some "dotnet build {project_file_q} -c Release", tryGetBuildCompileCommand CSharp)
    Assert.Equal(Some "dotnet run --project {project_file_q}", tryGetBuildRunCommand CSharp)
    Assert.Equal(Some "llvm-as {main_file_q} -o {main_bc_file_q}", tryGetBuildCompileCommand LLVM)
    Assert.Equal(Some "lli {main_file_q}", tryGetBuildRunCommand LLVM)
