// ASP.NET minimal API wiring for the ll-lang TODO demo.
//
// The ll-lang compiler emits `src/Main.cs` from `src/Main.lll`
// (static class `AspnetTodo_Main`). We consume that class here:
// - `AspnetTodo_Main.greeting()`      -> GET /hello
// - `AspnetTodo_Main.initial_todos()` -> GET /todos seed data
// - `AspnetTodo_Main.mk_todo`         -> POST /todos factory
//
// `Todo` is an interface with a single `MkTodo` record carrying
// positional fields `_0` (id, long), `_1` (title, string),
// `_2` (done, bool). We project those onto a JSON-friendly DTO.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");
var app = builder.Build();

// In-memory store seeded from ll-lang.
var store = new List<AspnetTodo_Main.Todo>(AspnetTodo_Main.initial_todos());
long nextId = store.Count + 1;
var gate = new object();

static TodoDto ToDto(AspnetTodo_Main.Todo t)
{
    var m = (AspnetTodo_Main.MkTodo)t;
    return new TodoDto(m._0, m._1, m._2);
}

app.MapGet("/hello", () => AspnetTodo_Main.greeting());

app.MapGet("/todos", () =>
{
    lock (gate) { return store.Select(ToDto).ToList(); }
});

app.MapGet("/todos/{id:long}", (long id) =>
{
    lock (gate)
    {
        var found = store.Select(ToDto).FirstOrDefault(t => t.Id == id);
        return found is null ? Results.NotFound() : Results.Ok(found);
    }
});

app.MapPost("/todos", (TodoCreateRequest req) =>
{
    lock (gate)
    {
        var id = nextId++;
        // ll-lang factory: mk_todo is curried (long -> string -> Todo).
        var todo = AspnetTodo_Main.mk_todo(id)(req.Title ?? "");
        store.Add(todo);
        return Results.Created($"/todos/{id}", ToDto(todo));
    }
});

app.Run();

record TodoDto(long Id, string Title, bool Done);
record TodoCreateRequest(string? Title);
