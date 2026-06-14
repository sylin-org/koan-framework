using Koan.Web.Admin.Contracts;

namespace Koan.Web.Admin.Services;

public interface IKoanAdminRouteProvider
{
    KoanAdminRouteMap Current { get; }
}
