using Sora.Core;
using Sora.Scheduling;

namespace S5.Recs.Tasks;

internal sealed class S5BootstrapTaskRegistration : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IScheduledTask, S5BootstrapTask>();
    }
}