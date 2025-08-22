namespace Sora.Messaging;

public interface IInboxDiscoveryPolicy
{
    bool ShouldDiscover(IServiceProvider sp);
    string Reason(IServiceProvider sp);
}