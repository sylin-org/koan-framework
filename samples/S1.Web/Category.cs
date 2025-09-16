using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class Category : Entity<Category>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}