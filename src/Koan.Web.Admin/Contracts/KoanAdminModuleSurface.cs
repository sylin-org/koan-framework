using System.Text.Json.Serialization;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminModuleSurface(
    string Name,
    string? Version,
    string? Description,
    string Pillar,
    string PillarColor,
    string PillarIcon,
    IReadOnlyList<KoanAdminModuleSurfaceSetting> Settings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<KoanAdminModuleSurfaceTool> Tools);

public sealed record KoanAdminModuleSurfaceSetting(
    string Key,
    string Label,
    string Description,
    string Value,
    bool Secret,
    KoanAdminSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers);

public sealed record KoanAdminModuleSurfaceTool(
    string Name,
    string Route,
    string? Description,
    string? Capability);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KoanAdminSettingSource
{
    Unknown,
    Auto,
    AppSettings,
    Environment,
    Custom
}
