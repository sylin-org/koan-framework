using Koan.Data.Core.Model;

namespace KoanAspireIntegration.Models;

/// <summary>
/// Sample Todo entity to demonstrate Koan-Aspire integration
/// with multi-provider data access (Postgres + Redis).
/// </summary>
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public void MarkComplete()
    {
        IsCompleted = true;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkIncomplete()
    {
        IsCompleted = false;
        CompletedAt = null;
    }
}