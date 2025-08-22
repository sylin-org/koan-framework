namespace Sora.Web.Attributes;

/// <summary>
/// Controls default data behaviors for an EntityController: pagination and limits.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SoraDataBehaviorAttribute : Attribute
{
    public bool MustPaginate { get; set; } = false;
    public int DefaultPageSize { get; set; } = Sora.Web.Infrastructure.SoraWebConstants.Defaults.DefaultPageSize;
    public int MaxPageSize { get; set; } = Sora.Web.Infrastructure.SoraWebConstants.Defaults.MaxPageSize;
}
