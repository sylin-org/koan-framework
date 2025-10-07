using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Testing.Extensions;

public static class TestContextServiceProviderExtensions
{
    public static ServiceProviderFixture GetServiceProviderFixture(this TestContext context, string key = "services")
        => context.GetRequiredItem<ServiceProviderFixture>(key);

    public static IServiceProvider GetServiceProvider(this TestContext context, string key = "services")
        => context.GetServiceProviderFixture(key).Services;

    public static IServiceScope CreateServiceScope(this TestContext context, string key = "services")
        => context.GetServiceProvider(key).CreateScope();
}
