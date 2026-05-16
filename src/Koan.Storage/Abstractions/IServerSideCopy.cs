namespace Koan.Storage.Abstractions;

public interface IServerSideCopy
{
    Task<bool> Copy(string sourceContainer, string sourceKey, string targetContainer, string targetKey, CancellationToken ct = default);
}