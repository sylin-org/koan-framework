using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Core;
using Sora.Messaging;

namespace Sora.Mq.RabbitMq.IntegrationTests.Fixtures;

public sealed class MessagingHost : IAsyncDisposable
{
    private readonly ServiceProvider _sp;

    private MessagingHost(ServiceProvider sp)
    {
        _sp = sp;
        _sp.UseSora();
    }

    public static MessagingHost Start(
        string connectionString,
        string exchange,
        Action<IServiceCollection> registerHandlers,
        string busCode = "rabbit",
        string group = "workers",
        params (string Key, string Value)[] extra)
    {
        var services = new ServiceCollection();
        var dict = new Dictionary<string, string?>
        {
            ["Sora:Messaging:DefaultBus"] = busCode,
            [$"Sora:Messaging:Buses:{busCode}:Provider"] = "RabbitMq",
            [$"Sora:Messaging:Buses:{busCode}:ConnectionString"] = connectionString,
            [$"Sora:Messaging:Buses:{busCode}:ProvisionOnStart"] = "true",
            [$"Sora:Messaging:Buses:{busCode}:RabbitMq:Exchange"] = exchange,
            [$"Sora:Messaging:Buses:{busCode}:RabbitMq:PublisherConfirms"] = "true",
            [$"Sora:Messaging:Buses:{busCode}:Subscriptions:0:Name"] = group,
            [$"Sora:Messaging:Buses:{busCode}:Subscriptions:0:RoutingKeys:0"] = "#",
        };
        foreach (var kv in extra)
            dict[kv.Key] = kv.Value;

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSora();
        registerHandlers(services);

        var sp = services.BuildServiceProvider();
        return new MessagingHost(sp);
    }

    public ValueTask DisposeAsync()
    {
        try { _sp?.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}
