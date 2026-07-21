public sealed record InvalidEvent;

public static class InvalidEventsConsumer
{
    public static Task Use(object value) => value.Events.Raise<InvalidEvent>();
}
