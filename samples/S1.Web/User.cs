using Koan.Data.Core.Model;

namespace S1.Web;

public sealed class User : Entity<User>
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
