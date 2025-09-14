using Sora.Data.Abstractions;
using Sora.Data.Core.Model;
using Sora.Data.Core.Relationships;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class TodoItem : Entity<TodoItem>
{
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; } = false;
    public int Priority { get; set; } = 0;

    [Parent(typeof(Todo))]
    public string TodoId { get; set; } = string.Empty;
}