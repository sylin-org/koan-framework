namespace Koan.Data.Abstractions;

public interface IEntity<TKey>
{
    TKey Id { get; }
}
