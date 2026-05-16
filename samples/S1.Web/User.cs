using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace S1.Web;

[DataAdapter("sqlite")]
public sealed class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}