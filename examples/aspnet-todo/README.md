# aspnet-todo — ll-lang + ASP.NET minimal API

A runnable TODO API written with ll-lang for the domain layer and ASP.NET
minimal API (net10.0) for the HTTP layer. Closes the demo tracked in #152.

## Status

Working end-to-end. Verified on `dotnet 10.0.201`:

```
GET  /hello             -> 200  "Hello from ll-lang"
GET  /todos             -> 200  [{id,title,done}, ...]
GET  /todos/{id}        -> 200 or 404
POST /todos  {"title"}  -> 201  {id,title,done:false}
```

## Layout

```
examples/aspnet-todo/
├── src/
│   └── Main.lll                    -- ll-lang domain + seed data
├── Program.cs                       -- ASP.NET route wiring (hand-written)
├── examples-aspnet-todo.csproj      -- Microsoft.NET.Sdk.Web project
└── README.md
```

### Why the split

`lllc` compiles a single `.lll` file to a single `.cs` file and emits a
sibling `.csproj` targeting `Microsoft.NET.Sdk` (library/exe, no web
references). The compiler's `external` mapping table in
`initial-compiler/src/LLLangCompiler/Platform.fs` is hard-coded and does
not include ASP.NET types, so we cannot (yet) call
`WebApplication.CreateBuilder` directly from ll-lang without touching the
frozen bootstrap compiler.

The pragmatic answer:

- ll-lang owns **types and pure data** — `Todo` ADT, seed list,
  factory function.
- C# owns **transport** — `WebApplication`, routing, JSON serialization,
  mutable in-memory store.
- Our own `.csproj` (`Sdk="Microsoft.NET.Sdk.Web"`) references both
  `src/Main.cs` (generated) and `Program.cs` (hand-written).
- The `Main.csproj` emitted by `lllc` as a sibling of `Main.cs` is
  **ignored** — it targets the non-web SDK and serves no purpose here.

## Build & run

From this directory:

```bash
# 1. Compile ll-lang -> C#. Produces src/Main.cs (and an unused src/Main.csproj).
dotnet ../../initial-compiler/src/LLLangTool/bin/Debug/net10.0/lllc.dll \
  build --target cs src/Main.lll

# 2. Build + run the ASP.NET project (pulls in src/Main.cs + Program.cs).
dotnet run --project examples-aspnet-todo.csproj
```

The server listens on `http://localhost:5000`.

## Try it

```bash
curl localhost:5000/hello
# Hello from ll-lang

curl localhost:5000/todos
# [{"id":1,"title":"Write ll-lang","done":true}, ...]

curl localhost:5000/todos/2
# {"id":2,"title":"Wire up ASP.NET minimal API","done":true}

curl -X POST -H 'Content-Type: application/json' \
  -d '{"title":"buy milk"}' localhost:5000/todos
# {"id":4,"title":"buy milk","done":false}
```

## How data flows

- `Todo = MkTodo Int Str Bool` in `Main.lll` becomes
  `AspnetTodo_Main.MkTodo(long _0, string _1, bool _2)` in the generated C#.
- `initial_todos()` returns `List<AspnetTodo_Main.Todo>`; `Program.cs`
  wraps each element into a `TodoDto(Id, Title, Done)` record so the
  JSON output has real field names instead of `_0`/`_1`/`_2`.
- `mk_todo(id)(title)` is the curried factory called by `POST /todos`.

## Known limitations

- **Match on single-constructor types**: the C# backend currently emits
  `default!` for functions whose body is a single-arm match
  (e.g. `| MkTodo id _ _ -> id`). For that reason this example does the
  field extraction in `Program.cs` rather than in `Main.lll`. A reduced
  repro: see `Main.lll` and how `Program.cs` casts to `MkTodo` and reads
  `._0/._1/._2` directly.
- **No FFI to ASP.NET**: there is no external mapping in the 1.x
  platform table for web-hosting primitives. A future expansion could
  add `aspnet_*` names to the C# external target map (or, per v2, let
  users define their own mappings) and move the routing layer into
  ll-lang.

## Clean rebuild

```bash
rm -rf bin obj src/Main.cs src/Main.csproj
dotnet ../../initial-compiler/src/LLLangTool/bin/Debug/net10.0/lllc.dll \
  build --target cs src/Main.lll
dotnet run --project examples-aspnet-todo.csproj
```
