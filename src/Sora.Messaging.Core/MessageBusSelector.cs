using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sora.Messaging.Infrastructure;

namespace Sora.Messaging;

internal sealed class MessageBusSelector : IMessageBusSelector
{
    private readonly IServiceProvider _sp;
    private readonly IEnumerable<IMessageBusFactory> _factories;
    private readonly IConfiguration _cfg;
    private readonly IOptions<MessagingOptions> _opts;

    public MessageBusSelector(IServiceProvider sp, IEnumerable<IMessageBusFactory> factories, IConfiguration cfg, IOptions<MessagingOptions> opts)
    { _sp = sp; _factories = factories; _cfg = cfg; _opts = opts; }

    public IMessageBus ResolveDefault(IServiceProvider sp)
    {
        var code = _opts.Value.DefaultBus ?? "default";
        return Resolve(sp, code);
    }

    public IMessageBus Resolve(IServiceProvider sp, string busCode)
    {
        // Pick factory by looking for a matching provider-specific section under the bus
        // Example: Sora:Messaging:Buses:{code}:RabbitMq exists -> use RabbitMq factory
        var sectionPath = $"{Constants.Configuration.Buses}:{busCode}";
        var busSection = _cfg.GetSection(sectionPath);
        IMessageBusFactory? selected = null;
        if (busSection.GetSection("RabbitMq").Exists())
        {
            selected = _factories.FirstOrDefault(f => string.Equals(f.ProviderName, "RabbitMq", StringComparison.OrdinalIgnoreCase));
        }
        // Fallback: prefer RabbitMq if available for better OOTB experience
        selected ??= _factories.FirstOrDefault(f => string.Equals(f.ProviderName, "RabbitMq", StringComparison.OrdinalIgnoreCase));
        // Finally: highest priority
        selected ??= _factories
            .OrderByDescending(f => f.ProviderPriority)
            .ThenBy(f => f.ProviderName)
            .FirstOrDefault();
        if (selected is null) throw new InvalidOperationException("No messaging providers registered.");
        var (bus, caps) = selected.Create(_sp, busCode, _cfg.GetSection(sectionPath));
        // Diagnostics are registered by providers when creating the bus
        return bus;
    }
}