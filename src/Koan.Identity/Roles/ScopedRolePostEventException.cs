namespace Koan.Identity.Roles;

public sealed class ScopedRolePostEventException : ScopedRoleException
{
    public ScopedRolePostEventException(string message, Exception innerException)
        : base("role.event.post.failed", message, innerException) { }
}
