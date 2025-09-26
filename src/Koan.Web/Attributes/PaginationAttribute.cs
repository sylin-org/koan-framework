namespace Koan.Web.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class PaginationAttribute : Attribute
{
    public PaginationMode Mode { get; set; } = PaginationMode.On;

    public int DefaultSize { get; set; } = Infrastructure.KoanWebConstants.Defaults.DefaultPageSize;

    public int MaxSize { get; set; } = Infrastructure.KoanWebConstants.Defaults.MaxPageSize;

    public bool IncludeCount { get; set; } = true;

    public string? DefaultSort { get; set; }
}
