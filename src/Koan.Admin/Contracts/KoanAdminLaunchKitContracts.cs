using System;
using System.Collections.Generic;

namespace Koan.Admin.Contracts;

public sealed record KoanAdminLaunchKitRequest(
    string? Profile,
    bool? IncludeAppSettings,
    bool? IncludeCompose,
    bool? IncludeAspire,
    bool? IncludeManifest,
    bool? IncludeReadme,
    IReadOnlyList<string>? OpenApiClients
);

public sealed record KoanAdminLaunchKitFile(
    string Path,
    string ContentType,
    long Length
);

public sealed record KoanAdminLaunchKitBundle(
    string Profile,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<KoanAdminLaunchKitFile> Files
);

public sealed record KoanAdminLaunchKitArchive(
    string FileName,
    string ContentType,
    KoanAdminLaunchKitBundle Bundle,
    byte[] Content
);

public sealed record KoanAdminLaunchKitMetadata(
    string DefaultProfile,
    IReadOnlyList<string> AvailableProfiles,
    IReadOnlyList<string> OpenApiClientTemplates,
    bool SupportsAppSettings,
    bool SupportsCompose,
    bool SupportsAspire,
    bool SupportsManifest,
    bool SupportsReadme
);
