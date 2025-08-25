using Microsoft.Extensions.DependencyInjection;

namespace Sora.Data.Cqrs.Outbox.Mongo;

[Abstractions.ProviderPriority(20)]
public sealed class MongoOutboxFactory : IOutboxStoreFactory
{
    public string Provider => "mongo";
    public IOutboxStore Create(IServiceProvider sp) => sp.GetRequiredService<MongoOutboxStore>();
}