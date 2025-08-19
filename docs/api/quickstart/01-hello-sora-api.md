# Hello Sora API

<div class="tutorial-header">
  <div class="tutorial-meta">
    <span class="time-estimate">⏱️ ~15 minutes</span>
    <span class="difficulty">Beginner</span>
    <span class="module">Module 1</span>
  </div>
</div>

## What You'll Build
A simple TaskFlow API that can create and retrieve tasks, using JSON file storage. By the end, you'll have a working REST API that responds to HTTP requests.

## Learning Outcomes
After completing this tutorial, you will:
- Understand Sora's entity and repository patterns
- Have a working API with GET and POST endpoints
- Know how Sora auto-configures storage and routing

## Prerequisites
- .NET 9 SDK installed
- Code editor (VS Code, Visual Studio, or Rider)

---

## Step 1: Create the Project

Use the Sora templates to bootstrap:

```bash
dotnet new install Sylin.Sora.Templates::0.1.0-preview
dotnet new sora-tiny-api -n TaskFlow
cd TaskFlow
```

## Step 2: Define Your Task Entity

```csharp
using Sora.Data.Core;

namespace TaskFlow.Models;

public class Task : Entity<Task>
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

## Step 3: Create the Controller

```csharp
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Models;

namespace TaskFlow.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Models.Task>>> GetTasks()
    {
        var tasks = await Models.Task.All();
        return Ok(tasks);
    }

    [HttpPost]
    public async Task<ActionResult<Models.Task>> CreateTask([FromBody] CreateTaskRequest request)
    {
        var task = new Models.Task
        {
            Title = request.Title,
            Description = request.Description
        };

        await task.Save();
        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, task);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Models.Task>> GetTask(Guid id)
    {
        var task = await Models.Task.Get(id);
        return task == null ? NotFound() : Ok(task);
    }
}

public record CreateTaskRequest(string Title, string? Description);
```

## Step 4: Run Your API

```bash
dotnet run
```

Then test with curl or your favorite client. Visit `/swagger` for OpenAPI UI in Development.

---

Next: SQLite Upgrade → 02-sqlite-upgrade.md
