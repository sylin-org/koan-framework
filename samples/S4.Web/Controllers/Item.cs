using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace S4.Web.Controllers;

[DataAdapter("mongo")]
public sealed class Item : Entity<Item>
{
    public string Name { get; set; } = string.Empty;
}