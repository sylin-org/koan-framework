using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

namespace TaskGraph;

[Cacheable(120)]
public sealed class Category : Entity<Category>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
