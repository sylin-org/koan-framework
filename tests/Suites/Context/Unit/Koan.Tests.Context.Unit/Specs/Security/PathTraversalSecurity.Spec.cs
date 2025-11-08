using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DiscoveryService = Koan.Context.Services.Discovery;

namespace Koan.Tests.Context.Unit.Specs.Security;

/// <summary>
/// Security tests for path traversal vulnerabilities in file discovery
/// </summary>
/// <remarks>
/// Tests cover OWASP Path Traversal (CWE-22) protection:
/// - Absolute path validation
/// - Parent directory traversal (../)
/// - Symbolic link protection
/// - Hidden file filtering
/// - Path canonicalization
/// </remarks>
public class PathTraversalSecuritySpec
{
    private readonly DiscoveryService _discovery;

    public PathTraversalSecuritySpec()
    {
        _discovery = new DiscoveryService(NullLogger<DiscoveryService>.Instance);
    }

    [Fact]
    public async Task ShouldRejectAbsolutePaths()
    {
        // Arrange
        var maliciousPath = "/etc/passwd";

        // Act
        Func<Task> act = async () => await _discovery
            .DiscoverAsync(maliciousPath)
            .ToListAsync();

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*absolute path*");
    }

    [Fact]
    public async Task ShouldRejectParentDirectoryTraversal()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var maliciousPath = Path.Combine(testDir, "..", "..", "..", "etc", "passwd");

        try
        {
            // Act
            Func<Task> act = async () => await _discovery
                .DiscoverAsync(maliciousPath)
                .ToListAsync();

            // Assert
            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*parent directory traversal*");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldRejectNonExistentDirectory()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        Func<Task> act = async () => await _discovery
            .DiscoverAsync(nonExistentPath)
            .ToListAsync();

        // Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task ShouldFilterHiddenFiles()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var hiddenFile = Path.Combine(testDir, ".hidden.md");
        await File.WriteAllTextAsync(hiddenFile, "# Hidden content");

        var visibleFile = Path.Combine(testDir, "visible.md");
        await File.WriteAllTextAsync(visibleFile, "# Visible content");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath.StartsWith("."));
            files.Should().Contain(f => f.RelativePath == "visible.md");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldFilterHiddenDirectories()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var hiddenDir = Path.Combine(testDir, ".git");
        Directory.CreateDirectory(hiddenDir);
        await File.WriteAllTextAsync(Path.Combine(hiddenDir, "config"), "hidden");

        var visibleFile = Path.Combine(testDir, "README.md");
        await File.WriteAllTextAsync(visibleFile, "# README");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath.Contains(".git"));
            files.Should().Contain(f => f.RelativePath == "README.md");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldRejectSymbolicLinksOutsideProjectRoot()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var targetDir = Path.Combine(Path.GetTempPath(), "sensitive-data");
        Directory.CreateDirectory(targetDir);

        var symlinkPath = Path.Combine(testDir, "link");

        try
        {
            // Create symbolic link (Windows requires admin or developer mode)
            if (OperatingSystem.IsWindows())
            {
                // Skip test on Windows unless running as admin
                return;
            }

            File.CreateSymbolicLink(symlinkPath, targetDir);

            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath.Contains("link"));
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
                Directory.Delete(symlinkPath);
            Directory.Delete(testDir, recursive: true);
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldNormalizePathsToPreventBypass()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        // Create path with mixed separators and redundant segments
        var messyPath = testDir + "/./subdir/../" + Path.DirectorySeparatorChar + "file.md";
        var normalizedDir = Path.GetDirectoryName(messyPath)!;

        Directory.CreateDirectory(normalizedDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "file.md"), "content");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().HaveCount(1);
            files[0].RelativePath.Should().Be("file.md");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldRejectNullOrEmptyPath()
    {
        // Act & Assert
        Func<Task> actNull = async () => await _discovery
            .DiscoverAsync(null!)
            .ToListAsync();

        await actNull.Should().ThrowAsync<ArgumentException>();

        Func<Task> actEmpty = async () => await _discovery
            .DiscoverAsync("")
            .ToListAsync();

        await actEmpty.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShouldRejectPathsWithNullBytes()
    {
        // Arrange
        var maliciousPath = "/tmp/test\0/../../etc/passwd";

        // Act
        Func<Task> act = async () => await _discovery
            .DiscoverAsync(maliciousPath)
            .ToListAsync();

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ShouldEnforceMaxPathLength()
    {
        // Arrange
        var longPath = Path.Combine(
            Path.GetTempPath(),
            new string('a', 300)); // Exceed typical max path

        // Act
        Func<Task> act = async () => await _discovery
            .DiscoverAsync(longPath)
            .ToListAsync();

        // Assert - Should throw either PathTooLongException or DirectoryNotFoundException
        await act.Should().ThrowAsync<Exception>()
            .Where(ex => ex is PathTooLongException or DirectoryNotFoundException);
    }

    [Fact]
    public async Task ShouldOnlyAllowMarkdownAndCodeFiles()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        await File.WriteAllTextAsync(Path.Combine(testDir, "safe.md"), "markdown");
        await File.WriteAllTextAsync(Path.Combine(testDir, "safe.cs"), "code");
        await File.WriteAllTextAsync(Path.Combine(testDir, "dangerous.exe"), "binary");
        await File.WriteAllTextAsync(Path.Combine(testDir, "script.sh"), "script");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().Contain(f => f.RelativePath == "safe.md");
            files.Should().Contain(f => f.RelativePath == "safe.cs");
            files.Should().NotContain(f => f.RelativePath == "dangerous.exe");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldRespectMaxFileSizeLimit()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var largeFile = Path.Combine(testDir, "large.md");
        // Create 60MB file (exceeds 50MB limit)
        await File.WriteAllBytesAsync(largeFile, new byte[60 * 1024 * 1024]);

        var normalFile = Path.Combine(testDir, "normal.md");
        await File.WriteAllTextAsync(normalFile, "# Normal size");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath == "large.md");
            files.Should().Contain(f => f.RelativePath == "normal.md");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldFilterNodeModulesDirectory()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var nodeModules = Path.Combine(testDir, "node_modules");
        Directory.CreateDirectory(nodeModules);
        await File.WriteAllTextAsync(Path.Combine(nodeModules, "package.json"), "{}");

        var sourceFile = Path.Combine(testDir, "index.js");
        await File.WriteAllTextAsync(sourceFile, "console.log('test')");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath.Contains("node_modules"));
            files.Should().Contain(f => f.RelativePath == "index.js");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldFilterBinAndObjDirectories()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        var binDir = Path.Combine(testDir, "bin");
        Directory.CreateDirectory(binDir);
        await File.WriteAllTextAsync(Path.Combine(binDir, "assembly.dll"), "binary");

        var objDir = Path.Combine(testDir, "obj");
        Directory.CreateDirectory(objDir);
        await File.WriteAllTextAsync(Path.Combine(objDir, "temp.cs"), "temp");

        var sourceFile = Path.Combine(testDir, "Program.cs");
        await File.WriteAllTextAsync(sourceFile, "class Program {}");

        try
        {
            // Act
            var files = await _discovery
                .DiscoverAsync(testDir)
                .ToListAsync();

            // Assert
            files.Should().NotContain(f => f.RelativePath.Contains("bin"));
            files.Should().NotContain(f => f.RelativePath.Contains("obj"));
            files.Should().Contain(f => f.RelativePath == "Program.cs");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task ShouldHandleConcurrentAccessSafely()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        await File.WriteAllTextAsync(Path.Combine(testDir, "file1.md"), "content 1");
        await File.WriteAllTextAsync(Path.Combine(testDir, "file2.md"), "content 2");

        try
        {
            // Act - Run discovery concurrently
            var tasks = Enumerable.Range(0, 10)
                .Select(_ => _discovery.DiscoverAsync(testDir).ToListAsync().AsTask())
                .ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert - All should succeed with same results
            results.Should().AllSatisfy(files =>
            {
                files.Should().HaveCount(2);
            });
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
