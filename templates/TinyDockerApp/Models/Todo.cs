namespace TinyDockerApp.Models;

public sealed class Todo : Sora.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
