namespace Koan.Messaging;

public interface IMessageBusSelector
{
    IMessageBus ResolveDefault(IServiceProvider sp);
    IMessageBus Resolve(IServiceProvider sp, string busCode);
}