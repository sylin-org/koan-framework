using Koan.Core;
using Koan.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Context.Tasks;

/// <summary>
/// Auto-registers IndexingJobMaintenanceTask with Koan's scheduling infrastructure
/// </summary>
internal sealed class IndexingJobMaintenanceTaskRegistration : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, IndexingJobMaintenanceTask>();
    }
}
