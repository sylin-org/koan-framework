using Koan.Data.Core.Model;

namespace AotRelational;

/// <summary>One ordinary entity. Nothing here knows which relational store it lands in.</summary>
public sealed class Note : Entity<Note>
{
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Stamp { get; set; }
}
