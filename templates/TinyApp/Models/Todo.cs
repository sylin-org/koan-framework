namespace TinyApp.Models;

public sealed class Todo : Koan.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
