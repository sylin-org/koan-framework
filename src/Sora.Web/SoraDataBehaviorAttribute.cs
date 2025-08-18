using System;

namespace Sora.Web.Controllers;

/// <summary>
/// Controls default data behaviors for an EntityController: pagination and limits.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SoraDataBehaviorAttribute : Attribute
{
    public bool MustPaginate { get; set; } = false;
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 200;
}
