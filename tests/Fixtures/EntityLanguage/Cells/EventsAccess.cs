using Koan.Communication;

public sealed record TodoCompleted;

[EventDetailsRequired]
public sealed record TodoRejected(string Reason);

public sealed class RecordTodoCompletion : IHandleEntityEvent<Todo, TodoCompleted>
{
    public bool Where(Todo todo, EventOccurrence<TodoCompleted> occurrence) => true;

    public Task Handle(
        Todo todo,
        EventOccurrence<TodoCompleted> occurrence,
        CancellationToken ct) => Task.CompletedTask;
}

public static class EventsConsumer
{
    public static Task<EventAcceptance> One(Todo todo, CancellationToken ct)
        => todo.Events.Raise<TodoCompleted>(ct);

    public static Task<EventAcceptance> Many(IEnumerable<Todo> todos, CancellationToken ct)
        => todos.Events.Raise<TodoCompleted>(ct);

    public static Task<EventAcceptance> Stream(IAsyncEnumerable<Todo> todos, CancellationToken ct)
        => todos.Events.Raise<TodoCompleted>(ct);

    public static Task<EventAcceptance> Details(Todo todo, CancellationToken ct)
        => todo.Events.Raise(new TodoRejected("not ready"), ct);
}
