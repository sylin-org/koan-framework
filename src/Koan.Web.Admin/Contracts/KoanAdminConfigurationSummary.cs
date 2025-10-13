using System;
using System.Collections.Generic;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminConfigurationSummary(
    IReadOnlyList<KoanAdminPillarSummary> Pillars)
{
    public static KoanAdminConfigurationSummary Empty { get; } = new(Array.Empty<KoanAdminPillarSummary>());
}

public sealed record KoanAdminPillarSummary(
    string Pillar,
    string PillarClass,
    string Icon,
    string ColorHex,
    string ColorRgb,
    int ModuleCount,
    int SettingCount,
    int NoteCount
);
