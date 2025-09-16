// Boot Koan via DI; JSON adapter self-registers; DataService provides repos

using Microsoft.Extensions.DependencyInjection;
using S0.ConsoleJsonRepo;
using Koan.Data.Core;
using Koan.Data.Json;

var services = new ServiceCollection();
// If a path arg is supplied, direct JSON data there; helps tests use isolated temp dirs
if (args is { Length: > 0 } && !string.IsNullOrWhiteSpace(args[0]))
{
    services.PostConfigure<JsonDataOptions>(o => o.DirectoryPath = args[0]);
}
services.StartKoan();

var todo = await new Todo { Title = "buy milk" }.Save();
var item = await Todo.Get(todo.Id);


Console.WriteLine($"Created: {item}");

var result = await Todo.Batch()
    .Add(new Todo { Title = "task 1" })
    .Add(new Todo { Title = "task 2" })
    .Update(todo.Id, t => t.Title = "buy milk and bread")
    .Save();
Console.WriteLine($"Batch: +{result.Added} ~{result.Updated} -{result.Deleted}");

var all = await Todo.All();
Console.WriteLine($"Total items: {all.Count}");