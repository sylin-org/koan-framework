namespace Koan.Communication.Tests.Support;

internal static class CommunicationTestHost
{
    public static Task<IntegrationHost> Start<TState>(
        TState state,
        CancellationToken ct,
        Action<IServiceCollection>? configure = null)
        where TState : class
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
