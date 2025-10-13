using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminManifestService
{
    Task<KoanAdminManifest> BuildAsync(CancellationToken cancellationToken = default);
    Task<KoanAdminHealthDocument> GetHealthAsync(CancellationToken cancellationToken = default);
}
