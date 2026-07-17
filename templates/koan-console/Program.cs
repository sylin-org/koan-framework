using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using KoanConsoleApp;

using var app = new ServiceCollection().StartKoan();

var todo = await new Todo { Title = "buy milk" }.Save();
Console.WriteLine($"saved: {todo.Id}");

var loaded = await Todo.Get(todo.Id);
Console.WriteLine($"loaded: {loaded?.Title}");

await new Todo { Title = "walk the dog", Done = true }.Save();

Console.WriteLine("open todos:");
foreach (var t in await Todo.Query(t => !t.Done))
    Console.WriteLine($"  - {t.Title}");
