using Microsoft.AspNetCore.Mvc;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;

namespace TaskGraph;

[Route("api/todos")]
[KoanDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class TodoController : EntityController<Todo>
{
    [HttpPost("reset-demo")]
    public async Task<IActionResult> ResetDemo(CancellationToken ct)
    {
        await TodoItem.RemoveAll(RemoveStrategy.Safe, ct);
        await Todo.RemoveAll(RemoveStrategy.Safe, ct);
        await TaskGraph.User.RemoveAll(RemoveStrategy.Safe, ct);
        await Category.RemoveAll(RemoveStrategy.Safe, ct);

        User[] users =
        [
            new() { Id = "user-alex", Name = "Alex Rivera", Email = "alex@example.test" },
            new() { Id = "user-sam", Name = "Sam Chen", Email = "sam@example.test" }
        ];

        Category[] categories =
        [
            new() { Id = "category-work", Name = "Work", Description = "Work that advances the release." },
            new() { Id = "category-home", Name = "Home", Description = "Life outside the release." }
        ];

        Todo[] todos =
        [
            new()
            {
                Id = "todo-proposal",
                Title = "Review the project proposal",
                Description = "Confirm that scope and outcomes describe the same product.",
                UserId = users[0].Id,
                CategoryId = categories[0].Id
            },
            new()
            {
                Id = "todo-release-notes",
                Title = "Write release notes",
                Description = "Explain the value in language customers recognize.",
                UserId = users[0].Id,
                CategoryId = categories[0].Id
            },
            new()
            {
                Id = "todo-groceries",
                Title = "Buy groceries",
                Description = "Restock ingredients for dinner.",
                UserId = users[1].Id,
                CategoryId = categories[1].Id
            }
        ];

        TodoItem[] items =
        [
            new() { Id = "item-scope", TodoId = todos[0].Id, Description = "Check the scope", Priority = 1, IsCompleted = true },
            new() { Id = "item-outcomes", TodoId = todos[0].Id, Description = "Check the outcomes", Priority = 2 },
            new() { Id = "item-highlights", TodoId = todos[1].Id, Description = "Summarize the highlights", Priority = 1 },
            new() { Id = "item-produce", TodoId = todos[2].Id, Description = "Choose fresh produce", Priority = 1 }
        ];

        await users.Save(ct);
        await categories.Save(ct);
        await todos.Save(ct);
        await items.Save(ct);

        return Ok(new
        {
            users = users.Length,
            categories = categories.Length,
            todos = todos.Length,
            todoItems = items.Length,
            representativeTodoId = todos[0].Id
        });
    }

    [HttpGet("{id}/context")]
    public async Task<ActionResult<RelationshipGraph<Todo>>> Context(string id, CancellationToken ct)
    {
        var todo = await Todo.Get(id, ct);
        return todo is null ? NotFound() : Ok(await todo.Relatives(ct));
    }

    [HttpGet("relationships/set")]
    public async Task<ActionResult<IReadOnlyList<RelationshipGraph<Todo>>>> Set(
        [FromQuery] int limit = 3,
        CancellationToken ct = default)
    {
        var todos = await Todo.FirstPage(Math.Clamp(limit, 1, 20), ct);
        return Ok(await todos.Relatives(ct));
    }

    [HttpGet("relationships/stream")]
    public async Task<ActionResult<IReadOnlyList<RelationshipGraph<Todo>>>> Stream(
        [FromQuery] int limit = 3,
        CancellationToken ct = default)
    {
        var contexts = new List<RelationshipGraph<Todo>>();
        await foreach (var context in Todo.AllStream(batchSize: 2).Relatives(ct))
        {
            contexts.Add(context);
            if (contexts.Count >= Math.Clamp(limit, 1, 20))
            {
                break;
            }
        }

        return Ok(contexts);
    }
}
