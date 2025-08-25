namespace Sora.Storage.Abstractions;

public interface IPresignOperations
{
    Task<Uri> PresignReadAsync(string container, string key, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignWriteAsync(string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);
}