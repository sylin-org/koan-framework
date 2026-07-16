public sealed record TodoStarted;

public static class CommunicationFacetNamespaceConsumer
{
    public static async Task Use(Todo todo, CancellationToken ct)
    {
        await todo.Transport.Send(ct);
        await todo.Events.Raise<TodoStarted>(ct);
    }
}
