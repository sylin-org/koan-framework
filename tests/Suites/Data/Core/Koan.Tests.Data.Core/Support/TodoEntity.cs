using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Support;

/// <summary>
/// Shared test entity for all test suites.
/// </summary>
public sealed class TodoEntity : Entity<TodoEntity, string>
{
    [Identifier]
    public override string Id { get; set; } = default!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}
