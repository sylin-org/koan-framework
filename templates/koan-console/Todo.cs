using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace KoanConsoleApp;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";

    public bool Done { get; set; }
}
