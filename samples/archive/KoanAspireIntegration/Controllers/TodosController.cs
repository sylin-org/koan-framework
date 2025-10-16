using Microsoft.AspNetCore.Mvc;
using KoanAspireIntegration.Models;
using Koan.Data.Core;

namespace KoanAspireIntegration.Controllers;

/// <summary>
/// Sample controller demonstrating Koan-Aspire integration
/// with multi-provider data access patterns.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    private readonly ILogger<TodosController> _logger;

    public TodosController(ILogger<TodosController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get all todos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Todo>>> GetTodos()
    {
        try
        {
            var todos = await Todo.All();
            return Ok(todos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve todos");
            return StatusCode(500, "Failed to retrieve todos");
        }
    }

    /// <summary>
    /// Get a specific todo by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Todo>> GetTodo(string id)
    {
        try
        {
            var todo = await Todo.Get(id);
            if (todo == null)
            {
                return NotFound();
            }
            return Ok(todo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve todo {TodoId}", id);
            return StatusCode(500, "Failed to retrieve todo");
        }
    }

    /// <summary>
    /// Create a new todo
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Todo>> CreateTodo([FromBody] CreateTodoRequest request)
    {
        try
        {
            var todo = new Todo
            {
                Title = request.Title,
                Description = request.Description ?? ""
            };

            await todo.Save();
            _logger.LogInformation("Created todo {TodoId}: {Title}", todo.Id, todo.Title);

            return CreatedAtAction(nameof(GetTodo), new { id = todo.Id }, todo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create todo");
            return StatusCode(500, "Failed to create todo");
        }
    }

    /// <summary>
    /// Update an existing todo
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Todo>> UpdateTodo(string id, [FromBody] UpdateTodoRequest request)
    {
        try
        {
            var todo = await Todo.Get(id);
            if (todo == null)
            {
                return NotFound();
            }

            todo.Title = request.Title ?? todo.Title;
            todo.Description = request.Description ?? todo.Description;

            if (request.IsCompleted.HasValue)
            {
                if (request.IsCompleted.Value && !todo.IsCompleted)
                {
                    todo.MarkComplete();
                }
                else if (!request.IsCompleted.Value && todo.IsCompleted)
                {
                    todo.MarkIncomplete();
                }
            }

            await todo.Save();
            _logger.LogInformation("Updated todo {TodoId}", todo.Id);

            return Ok(todo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update todo {TodoId}", id);
            return StatusCode(500, "Failed to update todo");
        }
    }

    /// <summary>
    /// Delete a todo
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTodo(string id)
    {
        try
        {
            var todo = await Todo.Get(id);
            if (todo == null)
            {
                return NotFound();
            }

            await todo.Remove();
            _logger.LogInformation("Deleted todo {TodoId}", id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete todo {TodoId}", id);
            return StatusCode(500, "Failed to delete todo");
        }
    }

    /// <summary>
    /// Get system information for diagnostics
    /// </summary>
    [HttpGet("system-info")]
    public ActionResult GetSystemInfo()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.StartsWith("Koan.") == true)
                .Select(a => a.GetName().Name)
                .ToList();

            var info = new
            {
                Timestamp = DateTime.UtcNow,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                KoanEnvironment = Environment.GetEnvironmentVariable("Koan_ENV"),
                ContainerRuntime = Environment.GetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME"),
                InContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
                FrameworkVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                KoanModulesLoaded = assemblies,
                IntegrationStatus = "Koan-Aspire integration working - modules auto-discovered"
            };

            return Ok(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system info");
            return StatusCode(500, "Failed to get system info");
        }
    }
}

/// <summary>
/// Request model for creating a new todo
/// </summary>
public class CreateTodoRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request model for updating an existing todo
/// </summary>
public class UpdateTodoRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? IsCompleted { get; set; }
}