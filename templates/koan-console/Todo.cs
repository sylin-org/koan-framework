using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace KoanConsoleApp;

// An entity is a plain class deriving from Entity<T>. The string Id is a GUID v7,
// auto-generated on first Save(). Static verbs (Get/Query/All) and instance verbs
// (Save/Remove) come from the base — no repository to write.
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";

    public bool Done { get; set; }
}
