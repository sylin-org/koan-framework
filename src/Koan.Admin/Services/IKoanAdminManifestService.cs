using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminManifestService
{
    Task<KoanAdminManifest> Build(CancellationToken cancellationToken = default);
    Task<KoanAdminHealthDocument> GetHealth(CancellationToken cancellationToken = default);
}
