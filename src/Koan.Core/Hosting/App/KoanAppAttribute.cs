using System;

namespace Koan.Core.Hosting.App;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class KoanAppAttribute : Attribute
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? ContactEmail { get; init; }
    public string? SupportUrl { get; init; }
    public string[] Tags { get; init; } = [];
}
