using Koan.Web.Admin.Contracts;

namespace Koan.Web.Admin.Services;

public interface IKoanAdminLaunchKitService
{
    Task<KoanAdminLaunchKitMetadata> GetMetadata(CancellationToken cancellationToken = default);

    Task<KoanAdminLaunchKitArchive> GenerateArchive(
        KoanAdminLaunchKitRequest request,
        CancellationToken cancellationToken = default);
}
