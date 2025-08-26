using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging.RabbitMq.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Messaging.RabbitMq";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddRabbitMq();
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var busCode = Configuration.Read<string?>(cfg, Constants.Configuration.Keys.DefaultBus, "default") ?? "default";
        var exchange = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.Exchange(busCode), "sora");
        report.AddSetting("Exchange", exchange);
        var conn = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.ConnectionString(busCode), null);
        var connName = Configuration.Read<string?>(cfg, Infrastructure.Constants.Configuration.ConnectionStringName(busCode), null);
        if (!string.IsNullOrWhiteSpace(conn)) report.AddSetting("ConnectionString", conn, isSecret: true);
        if (!string.IsNullOrWhiteSpace(connName)) report.AddSetting("ConnectionStringName", connName, isSecret: false);
    }
}
