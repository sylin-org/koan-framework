namespace Koan.Storage.Abstractions;

public interface IPresignOperations
{
    Task<Uri> PresignRead(string container, string key, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignWrite(string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);
}