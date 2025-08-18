using Microsoft.Extensions.DependencyInjection;
using Sora.Core;

namespace Sora.Messaging;

public sealed class MessagingSoraInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Bind options and register selector; discovery-friendly
    services.AddMessagingCore();
    // Bind Inbox + Discovery options and policy (ADR-0026)
    services.AddInboxConfiguration();
    }
}
