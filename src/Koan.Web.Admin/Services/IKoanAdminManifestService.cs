using Koan.Web.Admin.Contracts;

namespace Koan.Web.Admin.Services;

public interface IKoanAdminManifestService
{
    Task<KoanAdminManifest> Build(CancellationToken cancellationToken = default);
    Task<KoanAdminHealthDocument> GetHealth(CancellationToken cancellationToken = default);
}
