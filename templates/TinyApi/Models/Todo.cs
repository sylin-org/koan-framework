using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;

namespace TinyApi.Models;

public sealed class Todo : Sora.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
