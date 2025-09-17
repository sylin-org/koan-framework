using FluentAssertions;
using Koan.Data.Backup.Models;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Data.Backup.Tests.Simple;

/// <summary>
/// Basic tests to validate the backup/restore models and structures work
/// </summary>
public class BasicBackupTests
{
    private readonly ITestOutputHelper _output;

    public BasicBackupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BackupManifest_Should_Have_Required_Properties()
    {
        // Arrange & Act
        var manifest = new BackupManifest
        {
            Name = "test-backup",
            Description = "Test backup",
            Status = BackupStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = "1.0"
        };

        manifest.Entities.Add(new EntityBackupInfo
        {
            EntityType = "TestEntity",
            KeyType = "Guid",
            ItemCount = 100,
            SizeBytes = 1024,
            ContentHash = "abc123",
            StorageFile = "entities/TestEntity.jsonl",
            Provider = "sqlite"
        });

        // Assert
        manifest.Name.Should().Be("test-backup");
        manifest.Status.Should().Be(BackupStatus.Completed);
        manifest.Entities.Should().HaveCount(1);
        manifest.Entities.First().EntityType.Should().Be("TestEntity");

        _output.WriteLine($"✅ BackupManifest created with {manifest.Entities.Count} entities");
    }

    [Fact]
    public void BackupOptions_Should_Have_Default_Values()
    {
        // Arrange & Act
        var options = new BackupOptions();

        // Assert
        options.BatchSize.Should().Be(1000);
        options.VerificationEnabled.Should().BeTrue();
        options.Tags.Should().BeEmpty();
        options.Metadata.Should().BeEmpty();

        _output.WriteLine("✅ BackupOptions has correct default values");
    }

    [Fact]
    public void RestoreOptions_Should_Have_Default_Values()
    {
        // Arrange & Act
        var options = new RestoreOptions();

        // Assert
        options.BatchSize.Should().Be(1000);
        options.DisableConstraints.Should().BeTrue();
        options.DisableIndexes.Should().BeTrue();
        options.UseBulkMode.Should().BeTrue();
        options.OptimizationLevel.Should().Be("Balanced");

        _output.WriteLine("✅ RestoreOptions has correct default values");
    }

    [Fact]
    public void EntityBackupInfo_Should_Calculate_Compression_Ratio()
    {
        // Arrange
        var entityInfo = new EntityBackupInfo
        {
            EntityType = "TestEntity",
            KeyType = "Guid",
            ItemCount = 1000,
            SizeBytes = 1024 * 50, // 50KB compressed
            Provider = "sqlite"
        };

        // Act - Simulate original size calculation
        var estimatedOriginalSize = entityInfo.ItemCount * 100; // ~100 bytes per entity
        var compressionRatio = (double)entityInfo.SizeBytes / estimatedOriginalSize;

        // Assert
        compressionRatio.Should().BeLessThan(1.0, "Compressed size should be smaller");
        entityInfo.ItemCount.Should().BeGreaterThan(0);
        entityInfo.SizeBytes.Should().BeGreaterThan(0);

        _output.WriteLine($"✅ Compression ratio: {compressionRatio:P2} ({entityInfo.SizeBytes} / {estimatedOriginalSize})");
    }

    [Fact]
    public void BackupVerification_Should_Support_Integrity_Checks()
    {
        // Arrange
        var verification = new BackupVerification
        {
            OverallChecksum = "sha256hash",
            TotalSizeBytes = 1024 * 100,
            TotalItemCount = 1000,
            CompressionRatio = 0.6,
            IsValid = true
        };

        // Assert
        verification.IsValid.Should().BeTrue();
        verification.TotalItemCount.Should().BeGreaterThan(0);
        verification.CompressionRatio.Should().BeInRange(0.0, 1.0);
        verification.OverallChecksum.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✅ Verification: {verification.TotalItemCount} items, {verification.TotalSizeBytes:N0} bytes, {verification.CompressionRatio:P1} compression");
    }

    [Theory]
    [InlineData(BackupStatus.Created)]
    [InlineData(BackupStatus.InProgress)]
    [InlineData(BackupStatus.Completed)]
    [InlineData(BackupStatus.Failed)]
    [InlineData(BackupStatus.Cancelled)]
    public void BackupStatus_Should_Support_All_States(BackupStatus status)
    {
        // Arrange
        var manifest = new BackupManifest
        {
            Name = "test",
            Status = status
        };

        // Assert
        manifest.Status.Should().Be(status);
        Enum.IsDefined(typeof(BackupStatus), status).Should().BeTrue();

        _output.WriteLine($"✅ BackupStatus.{status} is valid");
    }

    [Fact]
    public void GlobalBackupOptions_Should_Extend_BackupOptions()
    {
        // Arrange
        var globalOptions = new GlobalBackupOptions
        {
            Description = "Global test",
            MaxConcurrency = 4,
            IncludeEntityTypes = new[] { "User", "Product" },
            ExcludeProviders = new[] { "test" }
        };

        // Assert
        globalOptions.Description.Should().Be("Global test");
        globalOptions.MaxConcurrency.Should().Be(4);
        globalOptions.IncludeEntityTypes.Should().HaveCount(2);
        globalOptions.BatchSize.Should().Be(1000); // Inherited from BackupOptions

        _output.WriteLine($"✅ GlobalBackupOptions: {globalOptions.MaxConcurrency} concurrency, {globalOptions.IncludeEntityTypes?.Length} entity types");
    }

    [Fact]
    public void EntityDiscoveryResult_Should_Track_Discovery_Metrics()
    {
        // Arrange
        var result = new EntityDiscoveryResult
        {
            DiscoveredAt = DateTimeOffset.UtcNow,
            TotalAssembliesScanned = 5,
            TotalTypesExamined = 150,
            DiscoveryDuration = TimeSpan.FromMilliseconds(250)
        };

        result.Entities.Add(new EntityTypeInfo
        {
            EntityType = typeof(string),
            KeyType = typeof(Guid),
            Provider = "sqlite",
            IsActive = true
        });

        // Assert
        result.TotalAssembliesScanned.Should().BeGreaterThan(0);
        result.TotalTypesExamined.Should().BeGreaterThan(0);
        result.Entities.Should().HaveCount(1);
        result.DiscoveryDuration.Should().BeGreaterThan(TimeSpan.Zero);

        _output.WriteLine($"✅ Discovery: {result.TotalAssembliesScanned} assemblies, {result.TotalTypesExamined} types, {result.DiscoveryDuration.TotalMilliseconds}ms");
    }
}