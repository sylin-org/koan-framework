namespace Sora.Web.Extensions.Authorization;

public sealed class SoftDeletePolicy
{
    public string? ListDeleted { get; set; }
    public string? Delete { get; set; }
    public string? DeleteMany { get; set; }
    public string? Restore { get; set; }
    public string? RestoreMany { get; set; }
}