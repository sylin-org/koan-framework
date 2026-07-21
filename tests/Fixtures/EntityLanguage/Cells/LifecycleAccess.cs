public static class LifecycleAccessConsumer
{
    public static void Configure()
        => Todo.Lifecycle.AfterUpsert(_ => { });
}
