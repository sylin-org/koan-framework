using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Storage;
using Koan.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Koan.Data.Backup.Core;

public class BackupDiscoveryService : IBackupDiscoveryService
{
    private readonly IStorageService _storageService;
    private readonly BackupStorageService _backupStorageService;
    private readonly ILogger<BackupDiscoveryService> _logger;
    private readonly ConcurrentDictionary<string, BackupCatalog> _catalogCache = new();
    private readonly object _refreshLock = new();

    private static readonly Regex BackupFilePattern = new(@"^(.+)-(\d{8}-\d{6})\.zip$", RegexOptions.Compiled);

    public BackupDiscoveryService(
        IStorageService storageService,
        BackupStorageService backupStorageService,
        ILogger<BackupDiscoveryService> logger)
    {
        _storageService = storageService;
        _backupStorageService = backupStorageService;
        _logger = logger;
    }

    public async Task<BackupCatalog> DiscoverAllBackupsAsync(DiscoveryOptions? options = null, CancellationToken ct = default)
    {
        options ??= new DiscoveryOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting backup discovery across all storage profiles");

        var catalog = new BackupCatalog
        {
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Get all storage profiles to scan
            var profilesToScan = options.StorageProfiles ?? await GetAllStorageProfilesAsync(ct);

            // Discover backups from each profile with controlled concurrency
            using var semaphore = new SemaphoreSlim(options.MaxConcurrency);
            var discoveryTasks = profilesToScan.Select(async profile =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    return await DiscoverByStorageProfileAsync(profile, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            var profileCatalogs = await Task.WhenAll(discoveryTasks);

            // Combine all catalogs
            var allBackups = profileCatalogs.SelectMany(c => c.Backups).ToList();
            catalog.Backups = allBackups;
            catalog.TotalCount = allBackups.Count;
            catalog.DiscoveryDuration = stopwatch.Elapsed;

            // Calculate stats
            catalog.Stats = CalculateCatalogStats(allBackups);

            _logger.LogInformation("Discovery completed. Found {BackupCount} backups across {ProfileCount} storage profiles in {Duration}ms",
                allBackups.Count, profilesToScan.Length, stopwatch.ElapsedMilliseconds);

            // Cache the result if using cache
            if (options.UseFastPath)
            {
                var cacheKey = "all_profiles";
                _catalogCache.AddOrUpdate(cacheKey, catalog, (key, existing) =>
                {
                    // Only update if this discovery is newer
                    return catalog.DiscoveredAt > existing.DiscoveredAt ? catalog : existing;
                });
            }

            return catalog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup discovery failed");
            catalog.DiscoveryDuration = stopwatch.Elapsed;
            return catalog;
        }
    }

    public async Task<BackupCatalog> DiscoverByStorageProfileAsync(string storageProfile, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("Discovering backups in storage profile: {StorageProfile}", storageProfile);

        var catalog = new BackupCatalog
        {
            DiscoveredAt = DateTimeOffset.UtcNow
        };

        try
        {
            var backups = new List<BackupInfo>();

            // List backup files from storage using the new listing capability
            try
            {
                await foreach (var file in _storageService.ListObjectsAsync(storageProfile, "backups", null, ct))
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        var backupInfo = await CreateBackupInfoFromFile(storageProfile, file, ct);
                        if (backupInfo != null)
                        {
                            backups.Add(backupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process backup file {File} in profile {Profile}",
                            file.Key, storageProfile);
                    }
                }
            }
            catch (NotSupportedException)
            {
                _logger.LogWarning("Storage provider for profile {StorageProfile} does not support listing operations. Cannot discover backups automatically.", storageProfile);
                // Return empty list when provider doesn't support listing
            }

            catalog.Backups = backups;
            catalog.TotalCount = backups.Count;
            catalog.Stats = CalculateCatalogStats(backups);
            catalog.DiscoveryDuration = stopwatch.Elapsed;

            _logger.LogDebug("Found {BackupCount} backups in profile {StorageProfile} in {Duration}ms",
                backups.Count, storageProfile, stopwatch.ElapsedMilliseconds);

            return catalog;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover backups in storage profile {StorageProfile}", storageProfile);
            catalog.DiscoveryDuration = stopwatch.Elapsed;
            return catalog;
        }
    }

    public async Task<BackupCatalog> QueryBackupsAsync(BackupQuery query, CancellationToken ct = default)
    {
        // First discover all backups
        var allBackups = await DiscoverAllBackupsAsync(ct: ct);

        // Apply filters
        var filteredBackups = allBackups.Backups.AsEnumerable();

        if (query.Tags?.Any() == true)
        {
            filteredBackups = filteredBackups.Where(b => query.Tags.Any(tag => b.Tags.Contains(tag)));
        }

        if (query.EntityTypes?.Any() == true)
        {
            filteredBackups = filteredBackups.Where(b =>
                b.EntityTypes?.Any(et => query.EntityTypes.Contains(et)) == true);
        }

        if (query.StorageProfiles?.Any() == true)
        {
            filteredBackups = filteredBackups.Where(b => query.StorageProfiles.Contains(b.StorageProfile));
        }

        if (query.DateFrom.HasValue)
        {
            filteredBackups = filteredBackups.Where(b => b.CreatedAt >= query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            filteredBackups = filteredBackups.Where(b => b.CreatedAt <= query.DateTo.Value);
        }

        if (query.Statuses?.Any() == true)
        {
            filteredBackups = filteredBackups.Where(b => query.Statuses.Contains(b.Status));
        }

        if (query.HealthStatuses?.Any() == true)
        {
            filteredBackups = filteredBackups.Where(b =>
                b.HealthStatus.HasValue && query.HealthStatuses.Contains(b.HealthStatus.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm.ToLowerInvariant();
            filteredBackups = filteredBackups.Where(b =>
                b.Name.ToLowerInvariant().Contains(searchTerm) ||
                b.Description.ToLowerInvariant().Contains(searchTerm) ||
                b.Tags.Any(tag => tag.ToLowerInvariant().Contains(searchTerm)));
        }

        if (query.MinSizeBytes.HasValue)
        {
            filteredBackups = filteredBackups.Where(b => b.SizeBytes >= query.MinSizeBytes.Value);
        }

        if (query.MaxSizeBytes.HasValue)
        {
            filteredBackups = filteredBackups.Where(b => b.SizeBytes <= query.MaxSizeBytes.Value);
        }

        // Apply sorting
        if (!string.IsNullOrWhiteSpace(query.SortBy))
        {
            filteredBackups = query.SortBy.ToLowerInvariant() switch
            {
                "name" => query.SortDirection == SortDirection.Ascending
                    ? filteredBackups.OrderBy(b => b.Name)
                    : filteredBackups.OrderByDescending(b => b.Name),
                "createdat" => query.SortDirection == SortDirection.Ascending
                    ? filteredBackups.OrderBy(b => b.CreatedAt)
                    : filteredBackups.OrderByDescending(b => b.CreatedAt),
                "size" => query.SortDirection == SortDirection.Ascending
                    ? filteredBackups.OrderBy(b => b.SizeBytes)
                    : filteredBackups.OrderByDescending(b => b.SizeBytes),
                _ => filteredBackups.OrderByDescending(b => b.CreatedAt) // Default sort
            };
        }
        else
        {
            filteredBackups = filteredBackups.OrderByDescending(b => b.CreatedAt);
        }

        // Apply pagination
        var pagedBackups = filteredBackups.Skip(query.Skip).Take(query.Take).ToList();

        return new BackupCatalog
        {
            Backups = pagedBackups,
            TotalCount = filteredBackups.Count(),
            DiscoveredAt = allBackups.DiscoveredAt,
            Query = query,
            Stats = CalculateCatalogStats(pagedBackups),
            DiscoveryDuration = allBackups.DiscoveryDuration
        };
    }

    public async Task<BackupInfo?> GetBackupAsync(string backupId, CancellationToken ct = default)
    {
        // Try to find by exact ID match first
        var allBackups = await DiscoverAllBackupsAsync(ct: ct);
        var backup = allBackups.Backups.FirstOrDefault(b => b.Id == backupId);

        if (backup != null)
            return backup;

        // Try to find by name if not found by ID
        backup = allBackups.Backups.FirstOrDefault(b => b.Name == backupId);

        return backup;
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(string backupId, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new BackupValidationResult
        {
            BackupId = backupId,
            ValidatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var backup = await GetBackupAsync(backupId, ct);
            if (backup == null)
            {
                result.IsValid = false;
                result.Issues.Add("Backup not found");
                result.HealthStatus = BackupHealthStatus.Critical;
                return result;
            }

            // Try to open and validate the backup archive
            try
            {
                var backupPath = GenerateBackupPath(backup.Name, backup.CreatedAt);
                using var archive = await _backupStorageService.OpenBackupArchiveAsync(backupPath, backup.StorageProfile, ct);

                // Load and validate manifest
                var manifest = await _backupStorageService.LoadManifestAsync(archive, ct);

                // Basic validation checks
                if (manifest.Status != BackupStatus.Completed)
                {
                    result.Warnings.Add($"Backup status is {manifest.Status}, not Completed");
                    result.HealthStatus = BackupHealthStatus.Warning;
                }

                // TODO: Add more comprehensive validation (checksums, entity counts, etc.)
                result.IsValid = true;
                result.HealthStatus = result.HealthStatus == BackupHealthStatus.Unknown
                    ? BackupHealthStatus.Healthy
                    : result.HealthStatus;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add($"Archive validation failed: {ex.Message}");
                result.HealthStatus = BackupHealthStatus.Corrupted;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup validation failed for {BackupId}", backupId);
            result.IsValid = false;
            result.Issues.Add($"Validation error: {ex.Message}");
            result.HealthStatus = BackupHealthStatus.Critical;
        }
        finally
        {
            result.ValidationDuration = stopwatch.Elapsed;
        }

        return result;
    }

    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing backup catalog cache");

        lock (_refreshLock)
        {
            _catalogCache.Clear();
        }

        // Trigger a fresh discovery
        await DiscoverAllBackupsAsync(new DiscoveryOptions { UseFastPath = true }, ct);

        _logger.LogInformation("Backup catalog cache refreshed");
    }

    public async Task<BackupCatalogStats> GetCatalogStatsAsync(CancellationToken ct = default)
    {
        var catalog = await DiscoverAllBackupsAsync(ct: ct);
        return catalog.Stats;
    }

    private async Task<BackupInfo?> CreateBackupInfoFromFile(string storageProfile, StorageObjectInfo file, CancellationToken ct)
    {
        try
        {
            // Extract file information from StorageObjectInfo
            var fileName = Path.GetFileName(file.Key);
            var fileSize = file.Size;
            var lastModified = file.LastModified;

            // Parse backup name and timestamp from filename
            var match = BackupFilePattern.Match(fileName);
            if (!match.Success)
            {
                _logger.LogWarning("Backup file {FileName} does not match expected pattern", fileName);
                return null;
            }

            var backupName = match.Groups[1].Value;
            var timestamp = match.Groups[2].Value;

            // Try to parse the timestamp
            if (!DateTime.TryParseExact(timestamp, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var createdAt))
            {
                _logger.LogWarning("Could not parse timestamp {Timestamp} from backup file {FileName}", timestamp, fileName);
                createdAt = lastModified.DateTime;
            }

            // Try to load manifest for detailed information
            BackupManifest? manifest = null;
            try
            {
                // Use the full file key as backup path
                var backupPath = file.Key;
                using var archive = await _backupStorageService.OpenBackupArchiveAsync(backupPath, storageProfile, ct);
                manifest = await _backupStorageService.LoadManifestAsync(archive, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load manifest for backup {FileName}", fileName);
            }

            var backupInfo = new BackupInfo
            {
                Id = manifest?.Id ?? Guid.CreateVersion7().ToString(),
                Name = manifest?.Name ?? backupName,
                Description = manifest?.Description ?? string.Empty,
                Tags = manifest?.Labels ?? Array.Empty<string>(),
                CreatedAt = manifest?.CreatedAt ?? createdAt,
                CompletedAt = manifest?.CompletedAt,
                Status = manifest?.Status ?? BackupStatus.Unknown,
                SizeBytes = fileSize,
                EntityCount = manifest?.Entities.Count ?? 0,
                StorageProfile = storageProfile,
                EntityTypes = manifest?.Entities.Select(e => e.EntityType).Distinct().ToArray(),
                Providers = manifest?.Entities.Select(e => e.Provider).Distinct().ToArray(),
                HealthStatus = BackupHealthStatus.Unknown, // Will be determined during validation
                ArchiveStorageKey = !string.IsNullOrWhiteSpace(manifest?.ArchiveStorageKey) ? manifest.ArchiveStorageKey : file.Key,
                ArchiveFileName = fileName,
                Metadata = manifest?.Metadata ?? new Dictionary<string, string>()
            };

            return backupInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create BackupInfo from file in profile {StorageProfile}", storageProfile);
            return null;
        }
    }

    private BackupCatalogStats CalculateCatalogStats(IEnumerable<BackupInfo> backups)
    {
        var backupList = backups.ToList();

        return new BackupCatalogStats
        {
            TotalBackups = backupList.Count,
            TotalSizeBytes = backupList.Sum(b => b.SizeBytes),
            HealthyBackups = backupList.Count(b => b.HealthStatus == BackupHealthStatus.Healthy),
            BackupsRequiringAttention = backupList.Count(b =>
                b.HealthStatus == BackupHealthStatus.Warning ||
                b.HealthStatus == BackupHealthStatus.Critical ||
                b.HealthStatus == BackupHealthStatus.Corrupted),
            BackupsByStatus = backupList.GroupBy(b => b.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            BackupsByProvider = backupList
                .SelectMany(b => b.Providers ?? Array.Empty<string>())
                .GroupBy(p => p)
                .ToDictionary(g => g.Key, g => g.Count()),
            SizeByStorageProfile = backupList.GroupBy(b => b.StorageProfile).ToDictionary(g => g.Key, g => g.Sum(b => b.SizeBytes)),
            OldestBackup = backupList.Any() ? backupList.Min(b => b.CreatedAt) : null,
            NewestBackup = backupList.Any() ? backupList.Max(b => b.CreatedAt) : null
        };
    }

    private Task<string[]> GetAllStorageProfilesAsync(CancellationToken ct)
    {
        // This is a simplified implementation - in reality you'd get this from your storage configuration
        // For now, return a default profile
        return Task.FromResult(new[] { string.Empty }); // Empty string often represents the default profile
    }

    private string GenerateBackupPath(string backupName, DateTimeOffset createdAt)
    {
        return $"backups/{backupName}-{createdAt:yyyyMMdd-HHmmss}.zip";
    }

}



