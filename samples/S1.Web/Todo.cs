using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; } = false;

    [Parent(typeof(User))]
    public string UserId { get; set; } = string.Empty;

    [Parent(typeof(Category))]
    public string CategoryId { get; set; } = string.Empty;
}