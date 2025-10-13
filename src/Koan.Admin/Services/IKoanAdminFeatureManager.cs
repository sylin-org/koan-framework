using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminFeatureManager
{
    KoanAdminFeatureSnapshot Current { get; }
}
