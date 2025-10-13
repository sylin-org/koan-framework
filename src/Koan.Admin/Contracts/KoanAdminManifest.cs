using Koan.Core.Observability.Health;

namespace Koan.Admin.Contracts;

public sealed record KoanAdminManifest(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<KoanAdminModuleManifest> Modules,
    KoanAdminHealthDocument Health
)
{
    public KoanAdminManifestSummary ToSummary()
    {
        var summaries = Modules
            .Select(m => new KoanAdminModuleSummary(m.Name, m.Version, m.Settings.Count, m.Notes.Count))
            .ToList();
        return new KoanAdminManifestSummary(GeneratedAtUtc, summaries, Health.Overall, Modules.Count);
    }
}

public sealed record KoanAdminModuleManifest(
    string Name,
    string? Version,
    IReadOnlyList<KoanAdminModuleSetting> Settings,
    IReadOnlyList<string> Notes
);

public sealed record KoanAdminModuleSetting(string Key, string Value, bool Secret);

public sealed record KoanAdminManifestSummary(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<KoanAdminModuleSummary> Modules,
    HealthStatus OverallHealth,
    int ModuleCount
);

public sealed record KoanAdminModuleSummary(
    string Name,
    string? Version,
    int SettingCount,
    int NoteCount
);
