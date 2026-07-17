using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using S0.ConsoleJsonRepo;

using var app = new ServiceCollection().StartKoan();

await Todo.RemoveAll(RemoveStrategy.Safe);

Todo[] checklist =
[
    new() { Title = "Buy milk" },
    new() { Title = "Walk the dog" },
    new() { Title = "Review the release notes" }
];

await checklist.Save();
await checklist[0].Complete();

var open = await Todo.Query(todo => !todo.Done);

Console.WriteLine($"Checklist ready: {checklist.Length} total, {checklist.Length - open.Count} complete, {open.Count} open.");
foreach (var todo in open)
{
    Console.WriteLine($" - {todo.Title}");
}
