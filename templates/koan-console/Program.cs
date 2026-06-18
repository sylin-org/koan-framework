using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using KoanConsoleApp;

// One line boots Koan: builds the provider, loads appsettings.json, runs discovery, and sets
// the ambient host so the entity static verbs work. Reference = Intent — no manual registration.
new ServiceCollection().StartKoan();

var todo = await new Todo { Title = "buy milk" }.Save();   // create (GUID v7 id, auto)
Console.WriteLine($"saved: {todo.Id}");

var loaded = await Todo.Get(todo.Id);                       // read by id
Console.WriteLine($"loaded: {loaded?.Title}");

await new Todo { Title = "walk the dog", Done = true }.Save();

Console.WriteLine("open todos:");
foreach (var t in await Todo.Query(t => !t.Done))          // query by predicate
    Console.WriteLine($"  - {t.Title}");
