using Koan.Data.Core.Selection;

public static class EntityCardinalityAccessConsumer
{
    public static IAsyncEnumerable<Todo> One(Todo todo)
        => EntityCardinality.One(todo);

    public static IAsyncEnumerable<Todo> Many(IEnumerable<Todo> todos)
        => EntityCardinality.Many(todos);

    public static IAsyncEnumerable<Todo> Stream(IAsyncEnumerable<Todo> todos)
        => EntityCardinality.Stream(todos);
}
