using System.Collections.Generic;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminModuleSurface(
    string Name,
    string? Version,
    IReadOnlyList<KoanAdminModuleSurfaceSetting> Settings,
    IReadOnlyList<string> Notes,
    string Pillar,
    string PillarClass,
    string ModuleClass,
    string Icon,
    string ColorHex,
    string ColorRgb
);

public sealed record KoanAdminModuleSurfaceSetting(
    string Key,
    string Value,
    bool Secret
);

public sealed record KoanAdminStartupNote(
    string Module,
    string Note
);
