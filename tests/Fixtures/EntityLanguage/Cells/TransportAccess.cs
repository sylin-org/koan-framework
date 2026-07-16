using Koan.Communication;

public sealed class ImportTodo : IReceiveEntity<Todo>
{
    public bool Where(Todo todo) => true;

    public Task Receive(Todo todo, CancellationToken ct) => Task.CompletedTask;
}

public static class TransportConsumer
{
    public static Task<TransportAcceptance> One(Todo todo, CancellationToken ct)
        => todo.Transport.Send(ct);

    public static Task<TransportAcceptance> Many(IEnumerable<Todo> todos, CancellationToken ct)
        => todos.Transport.Send(ct);

    public static Task<TransportAcceptance> Stream(IAsyncEnumerable<Todo> todos, CancellationToken ct)
        => todos.Transport.Send(ct);
}
