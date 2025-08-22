namespace Sora.Data.Abstractions;

public interface IEntity<TKey>
{
    TKey Id { get; }
}
