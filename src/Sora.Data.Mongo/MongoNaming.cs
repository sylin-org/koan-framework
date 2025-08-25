using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Mongo;

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