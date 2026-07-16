public static class RelationshipsAccessConsumer
{
    public static Task<RelationshipGraph<Todo>> Scalar(Todo todo)
        => todo.Relatives();

    public static Task<IReadOnlyList<RelationshipGraph<Todo>>> Many(IEnumerable<Todo> todos)
        => todos.Relatives();

    public static IAsyncEnumerable<RelationshipGraph<Todo>> Stream(IAsyncEnumerable<Todo> todos)
        => todos.Relatives();

    public static Task<IReadOnlyList<RelationshipGraph<CustomKeyEntity>>> CustomKeys(
        IEnumerable<CustomKeyEntity> entities)
        => entities.Relatives();

    public sealed class CustomKeyEntity : Entity<CustomKeyEntity, int>
    {
    }
}
