using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; } = false;

    [Parent(typeof(User))]
    public string UserId { get; set; } = "";

    [Parent(typeof(Category))]
    public string CategoryId { get; set; } = "";
}