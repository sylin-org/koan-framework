using Koan.Data.Core.Model;

namespace S10.DevPortal.Models;

/// <summary>
/// Basic user entity for authentication demo
/// </summary>
public class User : Entity<User>
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}