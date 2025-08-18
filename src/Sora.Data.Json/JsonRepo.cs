using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;

namespace Sora.Data.Json;

// Minimal, dev-friendly factory to get a repository without DI
public static class JsonRepo
{
    public static IDataRepository<TEntity, TKey> New<TEntity, TKey>(string directoryPath = ".\\data")
    where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = Options.Create(new JsonDataOptions { DirectoryPath = directoryPath });
        return new JsonRepository<TEntity, TKey>(opts);
    }
}
