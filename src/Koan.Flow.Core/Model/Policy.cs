using Koan.Data.Core.Model;

namespace Koan.Flow.Model;

public sealed class PolicyBundle : Entity<PolicyBundle>
{
    public string Name { get => Id; set => Id = value; }
    public string Version { get; set; } = "1";
    public object? Content { get; set; }
}
