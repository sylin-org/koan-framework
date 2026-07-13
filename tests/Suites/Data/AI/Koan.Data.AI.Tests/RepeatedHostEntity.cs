using Koan.Data.Core.Model;

namespace Koan.Data.AI.Tests;

public sealed class RepeatedHostEntity : Entity<RepeatedHostEntity, string>
{
    public string Value { get; set; } = "";
}
