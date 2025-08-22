using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;

namespace Sora.Messaging.RabbitMq.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Messaging.RabbitMq";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddRabbitMq();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var busCode = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.Core.Infrastructure.Constants.Configuration.Keys.DefaultBus, "default") ?? "default";
        var exchange = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.RabbitMq.Infrastructure.Constants.Configuration.Exchange(busCode), "sora");
        report.AddSetting("Exchange", exchange);
        var conn = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.RabbitMq.Infrastructure.Constants.Configuration.ConnectionString(busCode), null);
        var connName = Sora.Core.Configuration.Read<string?>(cfg, Sora.Messaging.RabbitMq.Infrastructure.Constants.Configuration.ConnectionStringName(busCode), null);
        if (!string.IsNullOrWhiteSpace(conn)) report.AddSetting("ConnectionString", conn, isSecret: true);
        if (!string.IsNullOrWhiteSpace(connName)) report.AddSetting("ConnectionStringName", connName, isSecret: false);
    }
}
