using Microsoft.Extensions.DependencyInjection;

namespace Sora.Core.Hosting.Bootstrap;

// New greenfield name for StartupProbeService; provides DI registration helper.
public static class BootProbeService
{
	public static IServiceCollection AddBootProbeService(this IServiceCollection services)
		=> services.AddHostedService<Sora.Core.StartupProbeService>();
}
