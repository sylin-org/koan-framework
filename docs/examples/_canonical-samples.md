---
status: draft
last_updated: 2025-10-09
framework_version: v0.6.3
---

# Canonical Code Samples

Use these snippets across docs to keep examples consistent and drift-free.

## Minimal Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

## Entity model and CRUD

```csharp
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

// Create
var todo = await new Todo { Title = "Buy milk" }.Save();

// Read by id
var loaded = await Todo.Get(todo.Id);

// Update
loaded.Completed = true;
await loaded.Save();

// Delete
await loaded.Delete();
```

## Controller (EntityController)

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

## Streaming & paging

```csharp
await foreach (var item in Todo.AllStream(ct))
{
    // process
}

var page = await Todo.FirstPage(pageSize: 50);
var next = await page.NextPage();
```

## AI usage (IAi)

```csharp
public class ChatController : ControllerBase
{
    private readonly IAi _ai;
    public ChatController(IAi ai) => _ai = ai;

    [HttpPost("/api/chat")] 
    public async Task<IActionResult> Chat([FromBody] string input)
    {
        var res = await _ai.ChatAsync(new AiChatRequest
        {
            Messages = [ new() { Role = AiMessageRole.User, Content = input } ]
        });
        return Ok(res.Choices?.FirstOrDefault()?.Message?.Content);
    }
}
```
