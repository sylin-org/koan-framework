using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Cqrs.Outbox.Connector.Mongo;

[Abstractions.ProviderPriority(20)]
public sealed class MongoOutboxFactory : IOutboxStoreFactory
{
    public string Provider => "mongo";
    public IOutboxStore Create(IServiceProvider sp) => sp.GetRequiredService<MongoOutboxStore>();
}
