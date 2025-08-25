namespace Sora.Messaging;

public sealed class Batch<T>
{
    public Batch() { Items = Array.Empty<T>(); }
    public Batch(IEnumerable<T> items) { Items = items is IReadOnlyList<T> list ? list : items.ToArray(); }
    public IReadOnlyList<T> Items { get; init; }
}