namespace Koan.Communication.Tests.Support;

internal static class TransportTestHost
{
    public static Task<IntegrationHost> Start(
        TransportTestState state,
        CancellationToken ct,
        Action<IServiceCollection>? configure = null)
        => KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .ConfigureServices(services =>
            {
                services.AddSingleton(state);
                services.AddKoan();
                configure?.Invoke(services);
            })
            .StartAsync(ct);
}
