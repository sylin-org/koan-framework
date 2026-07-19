using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Infrastructure;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Data.Core;
using Koan.Storage.Abstractions;
using Koan.Storage.Keys;

namespace Koan.Data.Backup.Core;

internal sealed class BackupService(IStorageService storage) : IBackupService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<BackupReceipt> Create<TEntity, TKey>(
        string name,
        BackupRequest? request = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        request ??= new BackupRequest();
        ValidateName(name);
        ValidateSize(request.PageSize, nameof(request.PageSize));

        var createdAt = DateTimeOffset.UtcNow;
        var archiveId = Guid.CreateVersion7().ToString();
        var sourcePartition = request.Partition ?? EntityContext.Current?.Partition;
        var descriptor = BackupArchiveNaming.Create(name, createdAt, archiveId);
        var tempPath = CreateTempPath();

        try
        {
            BackupArchiveManifest manifest;
            await using (var file = OpenTemporary(tempPath, FileMode.CreateNew))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var (recordCount, contentHash) = await WriteEntityData<TEntity, TKey>(
                        archive,
                        sourcePartition,
                        request.PageSize,
                        ct);

                    manifest = new BackupArchiveManifest(
                        BackupConstants.FormatVersion,
                        archiveId,
                        name.Trim(),
                        createdAt,
                        TypeIdentity(typeof(TEntity)),
                        TypeIdentity(typeof(TKey)),
                        sourcePartition,
                        recordCount,
                        BackupConstants.DataEntry,
                        contentHash);

                    await WriteManifest(archive, manifest, ct);
                }

                await file.FlushAsync(ct);
                file.Position = 0;

                using var scope = StorageScope.HostScoped();
                var stored = await storage.Put(
                    request.StorageProfile,
                    BackupConstants.ArchiveContainer,
                    descriptor.StorageKey,
                    file,
                    BackupConstants.ArchiveContentType,
                    ct);

                return new BackupReceipt(
                    manifest.ArchiveId,
                    manifest.Name,
                    request.StorageProfile,
                    descriptor.StorageKey,
                    manifest.CreatedAt,
                    manifest.SourcePartition,
                    manifest.RecordCount,
                    manifest.DataContentSha256,
                    stored.Size,
                    stored.ContentHash);
            }
        }
        finally
        {
            DeleteTemporary(tempPath);
        }
    }

    public async Task<RestoreReceipt> Restore<TEntity, TKey>(
        string storageKey,
        RestoreRequest? request = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        request ??= new RestoreRequest();
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ArgumentException("A backup storage key is required.", nameof(storageKey));
        ValidateSize(request.BatchSize, nameof(request.BatchSize));

        var tempPath = CreateTempPath();
        try
        {
            await Download(storageKey, request.StorageProfile, tempPath, ct);
            var manifest = await ValidateArchive<TEntity, TKey>(tempPath, ct);
            var targetPartition = request.TargetPartition ?? manifest.SourcePartition;
            var restored = await ApplyArchive<TEntity, TKey>(tempPath, targetPartition, request.BatchSize, ct);

            return new RestoreReceipt(
                manifest.ArchiveId,
                storageKey,
                targetPartition,
                restored,
                manifest.DataContentSha256,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            DeleteTemporary(tempPath);
        }
    }

    private static async Task<(int Count, string Sha256)> WriteEntityData<TEntity, TKey>(
        ZipArchive archive,
        string? partition,
        int pageSize,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var entry = archive.CreateEntry(BackupConstants.DataEntry, CompressionLevel.Optimal);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var partitionScope = partition is null ? null : EntityContext.With(partition: partition);

        var count = 0;
        await foreach (var entity in Data<TEntity, TKey>.AllStream(pageSize, ct).WithCancellation(ct))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(entity, Json);
            await output.WriteAsync(bytes, ct);
            await output.WriteAsync("\n"u8.ToArray(), ct);
            hash.AppendData(bytes);
            hash.AppendData("\n"u8);
            count++;
        }

        return (count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task WriteManifest(
        ZipArchive archive,
        BackupArchiveManifest manifest,
        CancellationToken ct)
    {
        var entry = archive.CreateEntry(BackupConstants.ManifestEntry, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await JsonSerializer.SerializeAsync(output, manifest, Json, ct);
    }

    private async Task Download(string storageKey, string storageProfile, string tempPath, CancellationToken ct)
    {
        Stream source;
        using (StorageScope.HostScoped())
        {
            source = await storage.Read(storageProfile, BackupConstants.ArchiveContainer, storageKey, ct);
        }

        await using (source)
        await using (var target = OpenTemporary(tempPath, FileMode.CreateNew))
        {
            await source.CopyToAsync(target, ct);
            await target.FlushAsync(ct);
        }
    }

    private static async Task<BackupArchiveManifest> ValidateArchive<TEntity, TKey>(
        string tempPath,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        try
        {
            await using var file = OpenTemporary(tempPath, FileMode.Open);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            var manifest = await ReadManifest(archive, ct);

            if (manifest.FormatVersion != BackupConstants.FormatVersion)
                throw new InvalidDataException($"Backup format {manifest.FormatVersion} is not supported.");
            if (string.IsNullOrWhiteSpace(manifest.ArchiveId)
                || string.IsNullOrWhiteSpace(manifest.Name)
                || manifest.RecordCount < 0
                || manifest.DataContentSha256?.Length != 64)
                throw new InvalidDataException("The backup manifest is incomplete.");
            if (!string.Equals(manifest.EntityType, TypeIdentity(typeof(TEntity)), StringComparison.Ordinal))
                throw new InvalidDataException($"The archive contains '{manifest.EntityType}', not '{TypeIdentity(typeof(TEntity))}'.");
            if (!string.Equals(manifest.KeyType, TypeIdentity(typeof(TKey)), StringComparison.Ordinal))
                throw new InvalidDataException($"The archive key type is '{manifest.KeyType}', not '{TypeIdentity(typeof(TKey))}'.");
            if (!string.Equals(manifest.DataEntry, BackupConstants.DataEntry, StringComparison.Ordinal))
                throw new InvalidDataException("The backup manifest identifies an unsupported data entry.");

            var (count, hash) = await InspectData<TEntity>(archive, manifest.DataEntry, ct);
            if (count != manifest.RecordCount)
                throw new InvalidDataException($"The archive declares {manifest.RecordCount} records but contains {count}.");
            if (!string.Equals(hash, manifest.DataContentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The backup data checksum does not match its manifest.");

            return manifest;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new InvalidDataException("The backup archive is malformed and no records were restored.", ex);
        }
    }

    private static async Task<BackupArchiveManifest> ReadManifest(ZipArchive archive, CancellationToken ct)
    {
        var entry = archive.GetEntry(BackupConstants.ManifestEntry)
            ?? throw new InvalidDataException("The backup archive has no manifest.");
        await using var input = entry.Open();
        return await JsonSerializer.DeserializeAsync<BackupArchiveManifest>(input, Json, ct)
            ?? throw new InvalidDataException("The backup manifest is empty.");
    }

    private static async Task<(int Count, string Sha256)> InspectData<TEntity>(
        ZipArchive archive,
        string entryName,
        CancellationToken ct)
        where TEntity : class
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;

        await foreach (var line in ReadLines(archive, entryName, ct))
        {
            try
            {
                _ = JsonSerializer.Deserialize<TEntity>(line, Json)
                    ?? throw new JsonException("An archived record deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Archived record {count + 1} is malformed.", ex);
            }

            var bytes = Encoding.UTF8.GetBytes(line);
            hash.AppendData(bytes);
            hash.AppendData("\n"u8);
            count++;
        }

        return (count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<int> ApplyArchive<TEntity, TKey>(
        string tempPath,
        string? targetPartition,
        int batchSize,
        CancellationToken ct)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        await using var file = OpenTemporary(tempPath, FileMode.Open);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        using var partitionScope = targetPartition is null ? null : EntityContext.With(partition: targetPartition);
        var batch = new List<TEntity>(batchSize);
        var restored = 0;

        await foreach (var line in ReadLines(archive, BackupConstants.DataEntry, ct))
        {
            batch.Add(JsonSerializer.Deserialize<TEntity>(line, Json)!);
            if (batch.Count < batchSize)
                continue;

            await Data<TEntity, TKey>.UpsertMany(batch, ct);
            restored += batch.Count;
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            await Data<TEntity, TKey>.UpsertMany(batch, ct);
            restored += batch.Count;
        }

        return restored;
    }

    private static async IAsyncEnumerable<string> ReadLines(
        ZipArchive archive,
        string entryName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"The backup archive has no '{entryName}' entry.");
        await using var input = entry.Open();
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException("The backup data contains an empty record.");
            yield return line;
        }
    }

    private static string TypeIdentity(Type type)
        => $"{type.Assembly.GetName().Name}:{type.FullName}";

    private static string CreateTempPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), BackupConstants.TempDirectory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.CreateVersion7():n}.zip");
    }

    private static FileStream OpenTemporary(string path, FileMode mode)
        => new(path, mode, mode == FileMode.Open ? FileAccess.Read : FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void DeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort only: the operation result must not be replaced by temporary-file cleanup failure.
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A backup name is required.", nameof(name));
    }

    private static void ValidateSize(int value, string parameter)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameter, value, "The batch size must be greater than zero.");
    }
}
