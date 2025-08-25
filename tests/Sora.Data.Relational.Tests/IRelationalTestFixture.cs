using Sora.Data.Abstractions;
using Sora.Data.Core;

namespace Sora.Data.Relational.Tests;

public interface IRelationalTestFixture<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    IDataService Data { get; }
    IServiceProvider ServiceProvider { get; }
    bool SkipTests { get; }
    string? SkipReason { get; }
}