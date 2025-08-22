using Sora.Data.Abstractions;
using Sora.Data.Core.Model;

namespace S2.Api.Controllers;

[DataAdapter("mongo")]
public sealed class Item : Entity<Item>
{
    public string Name { get; set; } = string.Empty;
}