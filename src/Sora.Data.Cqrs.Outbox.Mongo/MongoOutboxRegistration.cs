using Microsoft.Extensions.DependencyInjection;

namespace Sora.Data.Cqrs.Outbox.Mongo;

public static class MongoOutboxRegistration
{
    public static IServiceCollection AddMongoOutbox(this IServiceCollection services, Action<MongoOutboxOptions>? configure = null)
    {
        services.BindOutboxOptions<MongoOutboxOptions>("Mongo");
        if (configure is not null) services.PostConfigure(configure);
        services.AddSingleton<IOutboxStore, MongoOutboxStore>();
        services.AddSingleton<IOutboxStoreFactory, MongoOutboxFactory>();
        return services;
    }
}