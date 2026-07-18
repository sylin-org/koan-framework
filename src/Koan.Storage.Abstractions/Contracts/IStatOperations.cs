namespace Koan.Storage.Abstractions;

public interface IStatOperations
{
    Task<ObjectStat?> Head(string container, string key, CancellationToken ct = default);
}