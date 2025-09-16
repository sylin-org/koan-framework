namespace Koan.Data.Cqrs;

[Abstractions.ProviderPriority(0)]
public sealed class InMemoryOutboxFactory : IOutboxStoreFactory
{
    public string Provider => "inmemory";
    public IOutboxStore Create(IServiceProvider sp) => new InMemoryOutboxStore();
}