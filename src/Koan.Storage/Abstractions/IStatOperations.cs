namespace Koan.Storage.Abstractions;

public interface IStatOperations
{
    Task<ObjectStat?> HeadAsync(string container, string key, CancellationToken ct = default);
}