using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core;
using Sora.Data.Core.Extensions;
using Sora.Web.Attributes;
using Sora.Web.Controllers;

namespace S1.Web;

[Route("api/todo")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 10, MaxPageSize = 200)]
public sealed class TodoController : EntityController<Todo>
{
    [HttpPost("seed-with-relationships")]
    public async Task<IActionResult> SeedWithRelationships(CancellationToken ct)
    {
        // Get existing users and categories
        var users = await S1.Web.User.All(ct);
        var categories = await Category.All(ct);

        if (users.Count == 0 || categories.Count == 0)
        {
            return BadRequest(new { error = "Please seed users and categories first using /api/users/seed/5 and /api/categories/seed" });
        }

        var random = new Random();
        var todos = new List<Todo>();

        var taskTitles = new[]
        {
            "Review project proposal", "Update documentation", "Call client meeting", "Fix critical bug",
            "Plan vacation", "Buy groceries", "Exercise routine", "Read technical book", "Clean house",
            "Prepare presentation", "Code review", "Database backup", "Team standup", "Write tests"
        };

        // Create todos with random user and category assignments
        for (int i = 0; i < Math.Min(20, taskTitles.Length); i++)
        {
            var todo = new Todo
            {
                Title = taskTitles[i],
                Description = $"Detailed description for {taskTitles[i]}",
                IsCompleted = random.Next(0, 4) == 0, // 25% chance of being completed
                UserId = users[random.Next(users.Count)].Id,
                CategoryId = categories[random.Next(categories.Count)].Id
            };
            todos.Add(todo);
        }

        var upserted = await todos.Save(ct);

        // Create todo items for some todos
        var todoItems = new List<TodoItem>();
        var createdTodos = await Todo.All(ct);

        foreach (var todo in createdTodos.Take(10)) // Add items to first 10 todos
        {
            var itemCount = random.Next(2, 5); // 2-4 items per todo
            for (int j = 0; j < itemCount; j++)
            {
                var item = new TodoItem
                {
                    Description = $"Step {j + 1} for {todo.Title}",
                    IsCompleted = random.Next(0, 3) == 0, // 33% chance of being completed
                    Priority = random.Next(1, 4), // Priority 1-3
                    TodoId = todo.Id
                };
                todoItems.Add(item);
            }
        }

        var itemsUpserted = await todoItems.Save(ct);

        return Ok(new
        {
            seeded = new
            {
                todos = upserted,
                todoItems = itemsUpserted,
                users = users.Count,
                categories = categories.Count
            }
        });
    }

    [HttpPost("seed/{count}")]
    public async Task<IActionResult> Seed([FromRoute] int count, CancellationToken ct)
    {
        var items = Enumerable
            .Range(0, Math.Clamp(count, 1, 1000))
            .Select(_ => new Todo
            {
                Title = $"Task {Guid.NewGuid():N}",
                Description = "Basic todo item without relationships"
            });

        var upserted = await items.Save(ct);
        return Ok(new { seeded = upserted });
    }

    [HttpGet("relationship-demo/{id}")]
    public async Task<IActionResult> RelationshipDemo([FromRoute] string id, CancellationToken ct)
    {
        var todo = await Todo.Get(id, ct);
        if (todo == null) return NotFound();

        var demo = new
        {
            // Basic entity
            Entity = todo,

            // Single parent methods (semantic - validates cardinality)
            // This will throw since Todo has multiple parents
            SingleParentError = "Todo has multiple parents, so GetParent() would throw InvalidOperationException",

            // Typed parent methods
            User = await todo.GetParent<User>(ct),
            Category = await todo.GetParent<Category>(ct),

            // Explicit parent methods
            UserExplicit = await todo.GetParent<User>("UserId", ct),
            CategoryExplicit = await todo.GetParent<Category>("CategoryId", ct),

            // All parents
            AllParents = await todo.GetParents(ct),

            // Children
            TodoItems = await todo.GetChildren<TodoItem>(ct),

            // Full relationship graph
            RelationshipGraph = await todo.GetRelatives(ct)
        };

        return Ok(demo);
    }

    [HttpGet("streaming-demo")]
    public async Task<IActionResult> StreamingDemo(CancellationToken ct)
    {
        // Get first 5 todos
        var todos = await Todo.FirstPage(5, ct);

        // Demonstrate batch relationship loading
        var enrichedTodos = await todos.Relatives<Todo, string>(ct);

        // Demonstrate async streaming (for larger datasets)
        var streamingResults = new List<object>();
        await foreach (var enriched in Todo.AllStream(batchSize: 3).Relatives<Todo, string>(ct))
        {
            streamingResults.Add(new
            {
                TodoId = enriched.Entity.Id,
                Title = enriched.Entity.Title,
                ParentCount = enriched.Parents.Count,
                ChildrenTypes = enriched.Children.Keys.ToList()
            });

            // Limit for demo purposes
            if (streamingResults.Count >= 10) break;
        }

        return Ok(new
        {
            BatchEnriched = enrichedTodos,
            StreamingResults = streamingResults
        });
    }

    [HttpDelete("clear-all")]
    public async Task<IActionResult> ClearAll(CancellationToken ct)
    {
        var todoItemsDeleted = await TodoItem.RemoveAll(ct);
        var todosDeleted = await Todo.RemoveAll(ct);
        var usersDeleted = await S1.Web.User.RemoveAll(ct);
        var categoriesDeleted = await Category.RemoveAll(ct);

        return Ok(new {
            deleted = new
            {
                todoItems = todoItemsDeleted,
                todos = todosDeleted,
                users = usersDeleted,
                categories = categoriesDeleted
            }
        });
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var deleted = await Todo.RemoveAll(ct);
        return Ok(new { deleted });
    }
}

// Choose provider: default sqlite; to try json, replace with [DataAdapter("json")]