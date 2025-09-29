using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon.Runtime;

public static class CanonRuntimeRegistrationExtensions
{
	public static IServiceCollection AddInMemoryCanonRuntime(this IServiceCollection services)
	{
		services.TryAddSingleton<ICanonRuntime, InMemoryCanonRuntime>();
		return services;
	}

	public static ICanonRuntime GetCanonRuntime(this IServiceProvider provider)
	{
		return provider.GetRequiredService<ICanonRuntime>();
	}
}



