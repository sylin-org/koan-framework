using Koan.Cache;

public static class CacheConsumer
{
    public static EntityCacheExplanation Explain() => Todo.Cache.Explain();

    public static Task<EntityCacheEviction> One(Todo todo, CancellationToken ct)
        => todo.Cache.Evict(ct);

    public static Task<EntityCacheEviction> Many(IEnumerable<Todo> todos, CancellationToken ct)
        => todos.Cache.Evict(ct);

    public static Task<EntityCacheEviction> Stream(IAsyncEnumerable<Todo> todos, CancellationToken ct)
        => todos.Cache.Evict(ct);
}
