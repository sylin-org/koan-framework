using Sora.Core;
using Sora.Scheduling;

namespace S7.ContentPlatform.Tasks;

internal sealed class S7BootstrapTaskRegistration : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, S7BootstrapTask>();
    }
}
