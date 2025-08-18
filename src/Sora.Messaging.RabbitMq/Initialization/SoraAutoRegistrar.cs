using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Messaging.Inbox.Http;

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
    var section = cfg.GetSection($"{Sora.Messaging.Core.Infrastructure.Constants.Configuration.Buses}:default:RabbitMq");
        var exchange = section.GetValue<string?>("Exchange") ?? "sora";
        report.AddSetting("Exchange", exchange);
        var conn = section.GetValue<string?>("ConnectionString");
        var connName = section.GetValue<string?>("ConnectionStringName");
        if (!string.IsNullOrWhiteSpace(conn)) report.AddSetting("ConnectionString", conn, isSecret: true);
        if (!string.IsNullOrWhiteSpace(connName)) report.AddSetting("ConnectionStringName", connName, isSecret: false);
    }
}
