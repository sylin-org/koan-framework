using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// Test entity for transaction tests.
/// </summary>
internal sealed class TodoEntity : Entity<TodoEntity, string>
{
    [Identifier]
    public override string Id { get; set; } = default!;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}
