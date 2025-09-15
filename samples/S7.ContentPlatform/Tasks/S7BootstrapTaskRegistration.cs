using Koan.Core;
using Koan.Scheduling;

namespace S7.ContentPlatform.Tasks;

internal sealed class S7BootstrapTaskRegistration : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, S7BootstrapTask>();
    }
}
