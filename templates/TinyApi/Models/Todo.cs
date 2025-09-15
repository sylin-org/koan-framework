using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;

namespace TinyApi.Models;

public sealed class Todo : Koan.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
