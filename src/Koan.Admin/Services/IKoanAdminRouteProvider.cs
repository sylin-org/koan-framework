using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminRouteProvider
{
    KoanAdminRouteMap Current { get; }
}
