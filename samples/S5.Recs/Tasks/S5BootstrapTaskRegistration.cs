using Koan.Core;
using Koan.Scheduling;

namespace S5.Recs.Tasks;

internal sealed class S5BootstrapTaskRegistration : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, S5BootstrapTask>();
    }
}