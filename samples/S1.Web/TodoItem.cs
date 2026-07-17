using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace S1.Web;

public sealed class TodoItem : Entity<TodoItem>
{
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int Priority { get; set; }

    [Parent(typeof(Todo))]
    public string TodoId { get; set; } = string.Empty;
}
