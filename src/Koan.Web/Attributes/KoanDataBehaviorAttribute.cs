namespace Koan.Web.Attributes;

/// <summary>
/// Controls default data behaviors for an EntityController: pagination and limits.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class KoanDataBehaviorAttribute : Attribute
{
    public bool MustPaginate { get; set; } = false;
    public int DefaultPageSize { get; set; } = Infrastructure.KoanWebConstants.Defaults.DefaultPageSize;
    public int MaxPageSize { get; set; } = Infrastructure.KoanWebConstants.Defaults.MaxPageSize;
}
