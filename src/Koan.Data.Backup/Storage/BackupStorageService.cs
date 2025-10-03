using Koan.Data.Backup.Models;
using Koan.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Koan.Data.Backup.Storage;

public class BackupStorageService
{
    private readonly IStorageService _storageService;
    private readonly ILogger<BackupStorageService> _logger;
    private readonly JsonSerializerSettings _jsonSettings;

    public BackupStorageService(IStorageService storageService, ILogger<BackupStorageService> logger)
    {
        _storageService = storageService;
        _logger = logger;
        _jsonSettings = new JsonSerializerSettings
        {
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None
        };
    }

    /// <summary>
    /// Creates a backup archive stream for writing
    /// </summary>
    public Task<(Stream Stream, string BackupPath)> CreateBackupArchiveAsync(string backupName, string storageProfile, CancellationToken ct = default)
    {
        var backupPath = $"backups/{backupName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip";
        var stream = new MemoryStream(); // We'll use MemoryStream and upload when complete
        return Task.FromResult<(Stream, string)>((stream, backupPath));
    }

    /// <summary>
    /// Stores entity data in JSON Lines format within a ZIP archive
    /// </summary>
    public async Task<EntityBackupInfo> StoreEntityDataAsync<TEntity>(
        ZipArchive archive,
        string entityTypeName,
        string keyTypeName,
        string provider,
        IAsyncEnumerable<TEntity> entities,
        string set = "root",
        CancellationToken ct = default)
    {
        var fileName = set == "root" ? $"{entityTypeName}.jsonl" : $"{entityTypeName}#{set}.jsonl";
        var entry = archive.CreateEntry($"entities/{fileName}", CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);

        var count = 0;
        var totalBytes = 0L;
        var hasher = SHA256.Create();

        try
        {
            _logger.LogDebug("Starting backup of {EntityType} entities to {FileName}", entityTypeName, fileName);

            await foreach (var entity in entities.WithCancellation(ct))
            {
                var json = JsonConvert.SerializeObject(entity, _jsonSettings);
                var line = json + "\n";
                var bytes = Encoding.UTF8.GetBytes(line);

                await writer.WriteLineAsync(json);
                hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);

                count++;
                totalBytes += bytes.Length;

                if (count % 10000 == 0)
                {
                    _logger.LogDebug("Backed up {Count} {EntityType} entities...", count, entityTypeName);
                }
            }

            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var entityInfo = new EntityBackupInfo
            {
                EntityType = entityTypeName,
                KeyType = keyTypeName,
                Set = set,
                Provider = provider,
                ItemCount = count,
                SizeBytes = totalBytes,
                ContentHash = Convert.ToHexString(hasher.Hash!).ToLowerInvariant(),
                StorageFile = $"entities/{fileName}"
            };

            _logger.LogInformation("Completed backup of {Count} {EntityType} entities ({SizeKB} KB)",
                count, entityTypeName, totalBytes / 1024);

            return entityInfo;
        }
        finally
        {
            hasher.Dispose();
        }
    }

    /// <summary>
    /// Stores the backup manifest in the archive
    /// </summary>
    public async Task StoreManifestAsync(ZipArchive archive, BackupManifest manifest, CancellationToken ct = default)
    {
        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var manifestStream = manifestEntry.Open();

        var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        var bytes = Encoding.UTF8.GetBytes(json);
        await manifestStream.WriteAsync(bytes, ct);

        _logger.LogDebug("Stored backup manifest with {EntityCount} entities", manifest.Entities.Count);
    }

    /// <summary>
    /// Stores verification data in the archive
    /// </summary>
    public async Task StoreVerificationAsync(ZipArchive archive, BackupManifest manifest, CancellationToken ct = default)
    {
        // Create checksums file
        var checksums = manifest.Entities
            .Where(e => !string.IsNullOrEmpty(e.StorageFile) && !string.IsNullOrEmpty(e.ContentHash))
            .ToDictionary(
                e => e.StorageFile,
                e => e.ContentHash
            );

        var checksumsEntry = archive.CreateEntry("verification/checksums.json", CompressionLevel.Optimal);
        using var checksumsStream = checksumsEntry.Open();
        var checksumsJson = JsonConvert.SerializeObject(checksums, Formatting.Indented);
        var checksumsBytes = Encoding.UTF8.GetBytes(checksumsJson);
        await checksumsStream.WriteAsync(checksumsBytes, ct);

        // Create schema snapshots
        var schemas = manifest.Entities
            .Where(e => !string.IsNullOrEmpty(e.EntityType) && e.SchemaSnapshot != null)
            .ToDictionary(
                e => e.EntityType,
                e => e.SchemaSnapshot
            );

        var schemasEntry = archive.CreateEntry("verification/schema-snapshots.json", CompressionLevel.Optimal);
        using var schemasStream = schemasEntry.Open();
        var schemasJson = JsonConvert.SerializeObject(schemas, Formatting.Indented);
        var schemasBytes = Encoding.UTF8.GetBytes(schemasJson);
        await schemasStream.WriteAsync(schemasBytes, ct);

        _logger.LogDebug("Stored verification data for {EntityCount} entities", manifest.Entities.Count);
    }

    /// <summary>
    /// Uploads the completed backup archive to storage
    /// </summary>
    public async Task<string> UploadBackupArchiveAsync(Stream archiveStream, string backupPath, string storageProfile, CancellationToken ct = default)
    {
        archiveStream.Position = 0;

        var storageObject = await _storageService.PutAsync(
            storageProfile,
            "backups",
            backupPath,
            archiveStream,
            "application/zip",
            ct);

        _logger.LogInformation("Uploaded backup archive to {Path} ({SizeKB} KB)",
            backupPath, storageObject.Size / 1024);

        return storageObject.ContentHash ?? string.Empty;
    }

    /// <summary>
    /// Opens a backup archive for reading
    /// </summary>
    public async Task<ZipArchive> OpenBackupArchiveAsync(string backupPath, string storageProfile, CancellationToken ct = default)
    {
        var stream = await _storageService.ReadAsync(storageProfile, "backups", backupPath, ct);
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    /// <summary>
    /// Loads a backup manifest from an archive
    /// </summary>
    public async Task<BackupManifest> LoadManifestAsync(ZipArchive archive, CancellationToken ct = default)
    {
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
            throw new InvalidOperationException("Backup manifest not found");

        using var manifestStream = manifestEntry.Open();
        using var reader = new StreamReader(manifestStream);
        var json = await reader.ReadToEndAsync();

        var manifest = JsonConvert.DeserializeObject<BackupManifest>(json);
        return manifest ?? throw new InvalidOperationException("Invalid backup manifest");
    }

    /// <summary>
    /// Reads entity data from a backup archive as an async enumerable
    /// </summary>
    public async IAsyncEnumerable<T> ReadEntityDataAsync<T>(ZipArchive archive, string storageFile, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var entry = archive.GetEntry(storageFile);
        if (entry == null)
            throw new InvalidOperationException($"Entity data file {storageFile} not found in backup");

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            T? entity;
            try
            {
                entity = JsonConvert.DeserializeObject<T>(line, _jsonSettings);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Failed to deserialize entity at line {LineNumber} in {File}: {Error}",
                    lineNumber, storageFile, ex.Message);
                continue;
            }

            if (entity != null)
                yield return entity;
        }
    }

    /// <summary>
    /// Computes overall checksum for backup verification
    /// </summary>
    public string ComputeOverallChecksum(IEnumerable<EntityBackupInfo> entities)
    {
        var combined = string.Join("|", entities
            .OrderBy(e => e.EntityType)
            .ThenBy(e => e.Set)
            .Select(e => $"{e.EntityType}#{e.Set}:{e.ContentHash}"));

        using var hasher = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = hasher.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}