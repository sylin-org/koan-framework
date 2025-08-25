using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "Users")]
public sealed class UserDoc : Entity<UserDoc>
{
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record CreateUserRequest(string Name);
