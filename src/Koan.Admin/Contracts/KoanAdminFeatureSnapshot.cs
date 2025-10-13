namespace Koan.Admin.Contracts;

public sealed record KoanAdminFeatureSnapshot(
    bool Enabled,
    bool WebEnabled,
    bool ConsoleEnabled,
    bool ManifestExposed,
    bool AllowDestructiveOperations,
    bool AllowLogTranscriptDownload,
    bool LaunchKitEnabled,
    KoanAdminRouteMap Routes,
    string PathPrefix,
    bool DotPrefixAllowedInCurrentEnvironment
);
