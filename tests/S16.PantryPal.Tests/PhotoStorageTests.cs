using S16.PantryPal.Services;
using Koan.Storage.Abstractions;
using Koan.Storage;

namespace S16.PantryPal.Tests;

public class PhotoStorageTests
{
    private sealed class FakeStorageService : IStorageService
    {
        public List<(string Profile,string Container,string Key,string? ContentType, byte[] Bytes)> Writes { get; } = new();

        public Task<StorageObject> PutAsync(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Writes.Add((profile, container, key, contentType, ms.ToArray()));
            return Task.FromResult(new StorageObject { Id = key, Key = key, Name = key, ContentType = contentType, Size = ms.Length, CreatedAt = DateTimeOffset.UtcNow, Provider = "fake", Container = container, ContentHash = null, UpdatedAt = null, Tags = null });
        }
        public Task<Stream> ReadAsync(string profile, string container, string key, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Writes.First(w => w.Key == key).Bytes));
        public Task<(Stream Stream, long? Length)> ReadRangeAsync(string profile, string container, string key, long? from, long? to, CancellationToken ct = default)
            => Task.FromResult(((Stream)new MemoryStream(Writes.First(w => w.Key == key).Bytes), (long?)Writes.First(w => w.Key == key).Bytes.Length));
        public Task<bool> DeleteAsync(string profile, string container, string key, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string profile, string container, string key, CancellationToken ct = default) => Task.FromResult(Writes.Any(w => w.Key == key));
        public Task<ObjectStat?> HeadAsync(string profile, string container, string key, CancellationToken ct = default) => Task.FromResult<ObjectStat?>(null);
        public Task<StorageObject> TransferToProfileAsync(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default)
            => Task.FromResult(new StorageObject { Id = key, Key = key, Name = key, ContentType = null, Size = 0, CreatedAt = DateTimeOffset.UtcNow, Provider = "fake", Container = targetContainer ?? sourceContainer, ContentHash = null, UpdatedAt = null, Tags = null });
        public Task<Uri> PresignReadAsync(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Uri> PresignWriteAsync(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<StorageObjectInfo> ListObjectsAsync(string profile, string container, string? prefix = null, CancellationToken ct = default) => AsyncEnumerable.Empty<StorageObjectInfo>();
    }

    [Fact]
    public async Task StoreAsync_AssignsPrefixAndReturnsKey()
    {
        var svc = new FakeStorageService();
        var opts = new PhotoStorageOptions { Profile = "photos", Container = "pantry-photos", Prefix = "photos/" };
    IPhotoStorage ps = new PhotoStorage(svc, opts);
        await using var stream = new MemoryStream(new byte[] {1,2,3});
        var key = await ps.StoreAsync(stream, "test.jpg", "image/jpeg");
        key.Should().StartWith("photos/");
        svc.Writes.Should().ContainSingle(w => w.Key == key && w.ContentType == "image/jpeg");
    }
}