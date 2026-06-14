using Koan.Web.Admin.Contracts;

namespace Koan.Web.Admin.Services;

public interface IKoanAdminFeatureManager
{
    KoanAdminFeatureSnapshot Current { get; }
}
