using System;
using System.Collections.Generic;
using Koan.Admin.Contracts;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminStatusResponse(
    KoanEnvironmentSnapshot Environment,
    KoanAdminFeatureSnapshot Features,
    KoanAdminManifestSummary Manifest,
    KoanAdminHealthDocument Health,
    IReadOnlyList<KoanAdminModuleSurface> Modules,
    KoanAdminConfigurationSummary Configuration,
    IReadOnlyList<KoanAdminStartupNote> StartupNotes
)
{
    public static KoanAdminStatusResponse Disabled()
        => new(KoanEnv.CurrentSnapshot, new KoanAdminFeatureSnapshot(false, false, false, false, false, false, false,
            new KoanAdminRouteMap(Koan.Admin.Infrastructure.KoanAdminDefaults.Prefix, string.Empty, string.Empty),
            Koan.Admin.Infrastructure.KoanAdminDefaults.Prefix, KoanEnv.IsDevelopment),
            new KoanAdminManifestSummary(DateTimeOffset.UtcNow, Array.Empty<KoanAdminModuleSummary>(), HealthStatus.Unknown, 0),
            KoanAdminHealthDocument.Empty,
            Array.Empty<KoanAdminModuleSurface>(),
            KoanAdminConfigurationSummary.Empty,
            Array.Empty<KoanAdminStartupNote>());
}
