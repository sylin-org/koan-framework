using Koan.Identity.Roles;

namespace Koan.Data.Core.Model;

public static class Entity
{
    public static RoleLifecycleBuilder Role { get; } = new();
}
