using Sora.Data.Abstractions;
using Sora.Data.Core.Model;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class Category : Entity<Category>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}