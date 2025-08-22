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
        var selected = _factories
            .OrderByDescending(f => f.ProviderPriority)
            .ThenBy(f => f.ProviderName)
            .FirstOrDefault();
        if (selected is null) throw new InvalidOperationException("No messaging providers registered.");
        var sectionPath = $"{Constants.Configuration.Buses}:{busCode}";
        var (bus, caps) = selected.Create(_sp, busCode, _cfg.GetSection(sectionPath));
        // Diagnostics are registered by providers when creating the bus
        return bus;
    }
}