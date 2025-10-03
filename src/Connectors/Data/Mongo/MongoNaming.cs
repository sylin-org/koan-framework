using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Connector.Mongo;

internal static class MongoNaming
{
    public static string ResolveCollectionName(Type entityType, MongoOptions options)
    {
        var conv = new StorageNameResolver.Convention(
            options.NamingStyle,
            options.Separator ?? ".",
            NameCasing.AsIs
        );
        return StorageNameResolver.Resolve(entityType, conv);
    }
}
