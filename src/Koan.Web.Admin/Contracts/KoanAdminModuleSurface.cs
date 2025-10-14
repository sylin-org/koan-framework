using System.Collections.Generic;
using Koan.Admin.Contracts;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminModuleSurface(
    string Name,
    string? Version,
    string? Description,
    bool IsStub,
    IReadOnlyList<KoanAdminModuleSurfaceSetting> Settings,
    IReadOnlyList<string> Notes,
    string Pillar,
    string PillarClass,
    string ModuleClass,
    string Icon,
    string ColorHex,
    string ColorRgb,
    IReadOnlyList<KoanAdminModuleSurfaceTool> Tools
);

public sealed record KoanAdminModuleSurfaceSetting(
    string Key,
    string Value,
    bool Secret,
    KoanAdminSettingSource Source,
    string SourceKey,
    IReadOnlyList<string> Consumers
);

public sealed record KoanAdminModuleSurfaceTool(
    string Name,
    string Route,
    string? Description,
    string? Capability
);

public sealed record KoanAdminStartupNote(
    string Module,
    string Note
);
