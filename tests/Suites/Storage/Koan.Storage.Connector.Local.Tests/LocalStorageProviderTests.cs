using FluentAssertions;
using Koan.Storage.Abstractions;
using Koan.Storage.Connector.Local;
using Koan.Storage.Connector.Local.Infrastructure;
using Microsoft.Extensions.Options;
using System.Text;
using Xunit;

namespace Koan.Storage.Connector.Local.Tests;

/// <summary>
/// Comprehensive tests for LocalStorageProvider
/// Tests path traversal protection, sharding, atomic writes, range reads, etc.
/// </summary>
public class LocalStorageProviderTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly LocalStorageProvider _provider;
    private readonly LocalStorageOptions _options;

    public LocalStorageProviderTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), "koan-local-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBasePath);

        _options = new LocalStorageOptions { BasePath = _testBasePath };
        var optionsMonitor = new TestOptionsMonitor<LocalStorageOptions>(_options);
        _provider = new LocalStorageProvider(optionsMonitor);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBasePath))
                Directory.Delete(_testBasePath, recursive: true);
        }
        catch { /* Ignore cleanup errors */ }
    }

    #region Basic CRUD Operations

    [Fact(DisplayName = "LOCAL-001: Should write and read file successfully")]
    public async Task Should_Write_And_Read_File()
    {
        // Arrange
        const string container = "test-container";
        const string key = "test-file.txt";
        const string content = "Hello, Koan Storage!";

        // Act - Write
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Read
        string readContent;
        await using (var stream = await _provider.OpenReadAsync(container, key))
        using (var reader = new StreamReader(stream))
        {
            readContent = await reader.ReadToEndAsync();
        }

        // Assert
        readContent.Should().Be(content);
    }

    [Fact(DisplayName = "LOCAL-002: Should check file existence correctly")]
    public async Task Should_Check_Existence()
    {
        // Arrange
        const string container = "existence-test";
        const string existingKey = "exists.txt";
        const string missingKey = "missing.txt";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("test")))
        {
            await _provider.WriteAsync(container, existingKey, stream, "text/plain");
        }

        // Act
        var exists = await _provider.ExistsAsync(container, existingKey);
        var missing = await _provider.ExistsAsync(container, missingKey);

        // Assert
        exists.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact(DisplayName = "LOCAL-003: Should delete file successfully")]
    public async Task Should_Delete_File()
    {
        // Arrange
        const string container = "delete-test";
        const string key = "to-delete.txt";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("delete me")))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act
        var deleted = await _provider.DeleteAsync(container, key);
        var existsAfter = await _provider.ExistsAsync(container, key);

        // Assert
        deleted.Should().BeTrue();
        existsAfter.Should().BeFalse();
    }

    [Fact(DisplayName = "LOCAL-004: Should return false when deleting non-existent file")]
    public async Task Should_Return_False_When_Deleting_Nonexistent()
    {
        // Arrange
        const string container = "delete-test";
        const string key = "does-not-exist.txt";

        // Act
        var deleted = await _provider.DeleteAsync(container, key);

        // Assert
        deleted.Should().BeFalse();
    }

    #endregion

    #region Path Traversal Protection

    [Fact(DisplayName = "SECURITY-001: Should block path traversal with dot-dot")]
    public async Task Should_Block_Path_Traversal_DotDot()
    {
        // Arrange
        const string container = "secure-container";
        const string maliciousKey = "../../../etc/passwd";

        // Act
        Func<Task> act = async () =>
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hack"));
            await _provider.WriteAsync(container, maliciousKey, stream, null);
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Path traversal*");
    }

    [Fact(DisplayName = "SECURITY-002: Should block path traversal with dot segments")]
    public async Task Should_Block_Path_Traversal_Dot_Segments()
    {
        // Arrange
        const string container = "secure-container";
        const string maliciousKey = "foo/./../../secret.txt";

        // Act
        Func<Task> act = async () =>
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hack"));
            await _provider.WriteAsync(container, maliciousKey, stream, null);
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Path traversal*");
    }

    [Fact(DisplayName = "SECURITY-003: Should block invalid characters in key")]
    public async Task Should_Block_Invalid_Characters()
    {
        // Arrange
        const string container = "secure-container";
        const string invalidKey = "file<>|?.txt"; // Windows invalid chars

        // Act
        Func<Task> act = async () =>
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
            await _provider.WriteAsync(container, invalidKey, stream, null);
        };

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid characters*");
    }

    [Fact(DisplayName = "SECURITY-004: Should allow legitimate nested paths")]
    public async Task Should_Allow_Legitimate_Nested_Paths()
    {
        // Arrange
        const string container = "docs";
        const string key = "2025/01/report.pdf";

        // Act
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("PDF content")))
        {
            await _provider.WriteAsync(container, key, stream, "application/pdf");
        }

        var exists = await _provider.ExistsAsync(container, key);

        // Assert
        exists.Should().BeTrue();
    }

    #endregion

    #region Atomic Writes

    [Fact(DisplayName = "ATOMIC-001: Should use atomic write pattern (temp + rename)")]
    public async Task Should_Use_Atomic_Write_Pattern()
    {
        // Arrange
        const string container = "atomic-test";
        const string key = "atomic-file.txt";
        const string content = "atomic content";

        // Act - Write should use temp file + rename pattern
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Assert - File should exist and be readable
        var exists = await _provider.ExistsAsync(container, key);
        exists.Should().BeTrue();

        // Verify no temp files left behind
        var containerPath = Path.Combine(_testBasePath, container);
        var tempFiles = Directory.GetFiles(containerPath, "*.tmp-*", SearchOption.AllDirectories);
        tempFiles.Should().BeEmpty("temp files should be cleaned up after successful write");
    }

    [Fact(DisplayName = "ATOMIC-002: Should overwrite existing file atomically")]
    public async Task Should_Overwrite_Atomically()
    {
        // Arrange
        const string container = "overwrite-test";
        const string key = "overwrite.txt";

        // Write v1
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("version 1")))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Write v2
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("version 2")))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Assert - Should have v2 content
        string content;
        await using (var stream = await _provider.OpenReadAsync(container, key))
        using (var reader = new StreamReader(stream))
        {
            content = await reader.ReadToEndAsync();
        }

        content.Should().Be("version 2");
    }

    #endregion

    #region Range Reads

    [Fact(DisplayName = "RANGE-001: Should read byte range correctly")]
    public async Task Should_Read_Byte_Range()
    {
        // Arrange
        const string container = "range-test";
        const string key = "range-file.txt";
        const string content = "0123456789ABCDEF"; // 16 bytes

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Read bytes 5-10 (inclusive)
        var (stream, length) = await _provider.OpenReadRangeAsync(container, key, from: 5, to: 10);

        // Assert
        length.Should().Be(6); // 6 bytes: positions 5,6,7,8,9,10

        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            var rangeContent = await reader.ReadToEndAsync();
            rangeContent.Should().Be("56789A");
        }
    }

    [Fact(DisplayName = "RANGE-002: Should handle range from start")]
    public async Task Should_Read_From_Start()
    {
        // Arrange
        const string container = "range-test";
        const string key = "start-range.txt";
        const string content = "Hello, World!";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Read first 5 bytes
        var (stream, length) = await _provider.OpenReadRangeAsync(container, key, from: 0, to: 4);

        // Assert
        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            var rangeContent = await reader.ReadToEndAsync();
            rangeContent.Should().Be("Hello");
        }
    }

    [Fact(DisplayName = "RANGE-003: Should handle range to end")]
    public async Task Should_Read_To_End()
    {
        // Arrange
        const string container = "range-test";
        const string key = "end-range.txt";
        const string content = "Hello, World!";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Read from byte 7 to end
        var (stream, length) = await _provider.OpenReadRangeAsync(container, key, from: 7, to: null);

        // Assert
        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            var rangeContent = await reader.ReadToEndAsync();
            rangeContent.Should().Be("World!");
        }
    }

    #endregion

    #region Stat Operations

    [Fact(DisplayName = "STAT-001: Should return object stat with correct length")]
    public async Task Should_Return_Stat_With_Length()
    {
        // Arrange
        const string container = "stat-test";
        const string key = "stat-file.txt";
        const string content = "1234567890"; // 10 bytes

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act
        var statOps = (IStatOperations)_provider;
        var stat = await statOps.HeadAsync(container, key);

        // Assert
        stat.Should().NotBeNull();
        stat!.Length.Should().Be(10);
        stat.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "STAT-002: Should return null for non-existent file")]
    public async Task Should_Return_Null_Stat_For_Missing_File()
    {
        // Arrange
        const string container = "stat-test";
        const string key = "missing.txt";

        // Act
        var statOps = (IStatOperations)_provider;
        var stat = await statOps.HeadAsync(container, key);

        // Assert
        stat.Should().BeNull();
    }

    [Fact(DisplayName = "STAT-003: Should generate stable ETag")]
    public async Task Should_Generate_Stable_ETag()
    {
        // Arrange
        const string container = "etag-test";
        const string key = "etag-file.txt";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("content")))
        {
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - Get ETag twice
        var statOps = (IStatOperations)_provider;
        var stat1 = await statOps.HeadAsync(container, key);
        var stat2 = await statOps.HeadAsync(container, key);

        // Assert - Should be same (stable)
        stat1!.ETag.Should().Be(stat2!.ETag);
    }

    #endregion

    #region Server-Side Copy

    [Fact(DisplayName = "COPY-001: Should copy file within same container")]
    public async Task Should_Copy_Within_Container()
    {
        // Arrange
        const string container = "copy-test";
        const string sourceKey = "source.txt";
        const string targetKey = "target.txt";
        const string content = "copy this";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(container, sourceKey, stream, "text/plain");
        }

        // Act
        var copyOps = (IServerSideCopy)_provider;
        var copied = await copyOps.CopyAsync(container, sourceKey, container, targetKey);

        // Assert
        copied.Should().BeTrue();

        // Verify target exists and has same content
        await using (var stream = await _provider.OpenReadAsync(container, targetKey))
        using (var reader = new StreamReader(stream))
        {
            var targetContent = await reader.ReadToEndAsync();
            targetContent.Should().Be(content);
        }
    }

    [Fact(DisplayName = "COPY-002: Should copy file across containers")]
    public async Task Should_Copy_Across_Containers()
    {
        // Arrange
        const string sourceContainer = "source-container";
        const string targetContainer = "target-container";
        const string key = "file.txt";
        const string content = "cross-container copy";

        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await _provider.WriteAsync(sourceContainer, key, stream, "text/plain");
        }

        // Act
        var copyOps = (IServerSideCopy)_provider;
        var copied = await copyOps.CopyAsync(sourceContainer, key, targetContainer, key);

        // Assert
        copied.Should().BeTrue();
        var existsInTarget = await _provider.ExistsAsync(targetContainer, key);
        existsInTarget.Should().BeTrue();
    }

    #endregion

    #region List Operations

    [Fact(DisplayName = "LIST-001: Should list all objects in container")]
    public async Task Should_List_All_Objects()
    {
        // Arrange
        const string container = "list-test";
        var keys = new[] { "file1.txt", "file2.txt", "file3.txt" };

        foreach (var key in keys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content of {key}"));
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act
        var listOps = (IListOperations)_provider;
        var objects = await listOps.ListObjectsAsync(container).ToListAsync();

        // Assert
        objects.Should().HaveCount(3);
        objects.Select(o => o.Key).Should().BeEquivalentTo(keys);
    }

    [Fact(DisplayName = "LIST-002: Should filter by prefix")]
    public async Task Should_Filter_By_Prefix()
    {
        // Arrange
        const string container = "prefix-test";
        var allKeys = new[]
        {
            "2025/01/file1.txt",
            "2025/01/file2.txt",
            "2025/02/file3.txt",
            "2024/12/file4.txt"
        };

        foreach (var key in allKeys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
            await _provider.WriteAsync(container, key, stream, "text/plain");
        }

        // Act - List with prefix
        var listOps = (IListOperations)_provider;
        var filtered = await listOps.ListObjectsAsync(container, prefix: "2025/01/").ToListAsync();

        // Assert
        filtered.Should().HaveCount(2);
        filtered.Select(o => o.Key).Should().BeEquivalentTo(new[]
        {
            "2025/01/file1.txt",
            "2025/01/file2.txt"
        });
    }

    [Fact(DisplayName = "LIST-003: Should return empty list for empty container")]
    public async Task Should_Return_Empty_List_For_Empty_Container()
    {
        // Arrange
        const string container = "empty-container";

        // Act
        var listOps = (IListOperations)_provider;
        var objects = await listOps.ListObjectsAsync(container).ToListAsync();

        // Assert
        objects.Should().BeEmpty();
    }

    #endregion

    #region Provider Capabilities

    [Fact(DisplayName = "CAP-001: Should report correct provider name")]
    public void Should_Report_Provider_Name()
    {
        // Assert
        _provider.Name.Should().Be("local");
    }

    [Fact(DisplayName = "CAP-002: Should report correct capabilities")]
    public void Should_Report_Capabilities()
    {
        // Assert
        var caps = _provider.Capabilities;
        caps.SupportsSeek.Should().BeTrue("local storage supports seeking");
        caps.SupportsRangeReads.Should().BeTrue("local storage supports range reads");
        caps.SupportsPresignedUrls.Should().BeFalse("local storage doesn't support presigned URLs");
        caps.SupportsServerSideCopy.Should().BeTrue("local storage supports server-side copy");
    }

    #endregion
}

/// <summary>
/// Test implementation of IOptionsMonitor for testing
/// </summary>
internal class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
