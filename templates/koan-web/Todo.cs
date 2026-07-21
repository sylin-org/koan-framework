using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace KoanWebApp;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";

    public bool Done { get; set; }
}
