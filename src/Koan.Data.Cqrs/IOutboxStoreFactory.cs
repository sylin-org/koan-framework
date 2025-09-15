namespace Koan.Data.Cqrs;

/// <summary>
/// Factory for creating outbox stores; discovered via DI with ProviderPriority.
/// </summary>
public interface IOutboxStoreFactory
{
    string Provider { get; }
    IOutboxStore Create(IServiceProvider sp);
}