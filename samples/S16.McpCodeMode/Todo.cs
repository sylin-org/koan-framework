using Koan.Data.Core.Model;
using Koan.Mcp;

namespace S16.McpCodeMode;

[McpEntity(Name = "Todo", Description = "Task management entity")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string Priority { get; set; } = "medium";
}
