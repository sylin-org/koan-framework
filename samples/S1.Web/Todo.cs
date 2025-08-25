using Sora.Data.Abstractions;
using Sora.Data.Core.Model;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}