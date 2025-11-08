using Koan.Core;
using Koan.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Context.Tasks;

/// <summary>
/// Auto-registers JobMaintenanceTask with Koan's scheduling infrastructure
/// </summary>
internal sealed class JobMaintenanceTaskRegistration : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, JobMaintenanceTask>();
    }
}
