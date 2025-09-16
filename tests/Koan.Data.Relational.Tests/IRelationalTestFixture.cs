using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Data.Relational.Tests;

public interface IRelationalTestFixture<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    IDataService Data { get; }
    IServiceProvider ServiceProvider { get; }
    bool SkipTests { get; }
    string? SkipReason { get; }
}