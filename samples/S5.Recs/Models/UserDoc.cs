using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Optimization;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "Users")]
[OptimizeStorage(OptimizationType = StorageOptimizationType.None, Reason = "Uses human-readable string identifiers, not GUIDs")]
public sealed class UserDoc : Entity<UserDoc>
{
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}