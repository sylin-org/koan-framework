using Sora.Data.Abstractions;
using Sora.Data.Core.Model;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class User : Entity<User>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}