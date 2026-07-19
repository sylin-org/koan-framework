using Koan.Core;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminStatusResponse(
    DateTimeOffset CapturedAtUtc,
    KoanEnvironmentSnapshot Environment,
    KoanAdminRouteMap Routes,
    KoanAdminRuntimeSurface Runtime,
    KoanAdminHealthDocument Health,
    IReadOnlyList<KoanAdminModuleSurface> Modules);
