using Microsoft.Extensions.Configuration;

namespace Sora.Messaging;

public interface IMessageBusFactory
{
    int ProviderPriority { get; }
    string ProviderName { get; }
    (IMessageBus bus, IMessagingCapabilities caps) Create(IServiceProvider sp, string busCode, IConfiguration cfg);
}