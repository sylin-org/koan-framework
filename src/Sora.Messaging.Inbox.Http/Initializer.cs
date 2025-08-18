using Microsoft.Extensions.DependencyInjection;
using Sora.Core;

namespace Sora.Messaging.Inbox.Http;

// Auto-discovery initializer: when this package is referenced, it wires the HTTP inbox client from configuration
public sealed class HttpInboxSoraInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        services.AddHttpInboxFromConfig();
    }
}
