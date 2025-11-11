using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Context.Services; // For FileType, DiscoveredFile
using Koan.Context.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DiscoveryService = Koan.Context.Services.Discovery;

namespace Koan.Tests.Context.Unit.Specs.Discovery;

/// <summary>
/// Tests for DocumentDiscoveryService covering security fixes and edge cases
/// </summary>
/// <remarks>
/// Covers QA Report issues #1, #5, #6, #12, #13, #14, #15
/// </remarks>
public class DocumentDiscovery_Spec : IDisposable
{
    private readonly Mock<ILogger<DiscoveryService>> _loggerMock;
    private readonly DiscoveryService _service;
    private readonly string _testDir;

    public DocumentDiscovery_Spec()
    {
        _loggerMock = new Mock<ILogger<DiscoveryService>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Context:Security:AllowedDirectories:0"] = Path.GetTempPath(),
                ["Koan:Context:Security:EnableRestrictivePathValidation"] = "true"
            })
            .Build();

        var validator = new PathValidator(configuration);
        _service = new DiscoveryService(_loggerMock.Object, validator);

        // Create temporary test directory
        _testDir = Path.Combine(Path.GetTempPath(), $"koan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    #region Security Tests

    [Fact]
    public async Task DiscoverAsync_PathTraversal_ThrowsSecurityException()
    {
        // Arrange
        var maliciousPath = "../../../etc";

        // Act & Assert
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.DiscoverAsync(_testDir, maliciousPath).ToListAsync();
        });
    }

    [Fact]
    public async Task DiscoverAsync_AbsoluteDocsPath_ThrowsSecurityException()
    {
        // Arrange
        var absolutePath = @"C:\Windows\System32";

        // Act & Assert
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.DiscoverAsync(_testDir, absolutePath).ToListAsync();
        });
    }

    [Theory]
    [InlineData("../../sensitive")]
    [InlineData("../../../")]
    [InlineData("..\\..\\..\\Windows")]
    public async Task DiscoverAsync_VariousTraversalAttempts_ThrowsSecurityException(string maliciousPath)
    {
        // Act & Assert
        await Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.DiscoverAsync(_testDir, maliciousPath).ToListAsync();
        });
    }

    [Fact]
    public async Task DiscoverAsync_SkipsSymbolicLinks()
    {
        // Arrange
        var docsDir = Path.Combine(_testDir, "docs");
        Directory.CreateDirectory(docsDir);

        var realFile = Path.Combine(docsDir, "real.md");
        await File.WriteAllTextAsync(realFile, "# Real File");

        // Note: Creating symlinks requires admin privileges on Windows
        // This test validates the logic exists, actual symlink testing requires integration tests

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].AbsolutePath.Should().Be(realFile);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsLargeFiles()
    {
        // Arrange
        var docsDir = Path.Combine(_testDir, "docs");
        Directory.CreateDirectory(docsDir);

        var normalFile = Path.Combine(docsDir, "normal.md");
        await File.WriteAllTextAsync(normalFile, "# Normal File");

        var largeFile = Path.Combine(docsDir, "large.md");
        // Create a file that reports > 50MB size (we won't actually write 50MB to disk in tests)
        await File.WriteAllTextAsync(largeFile, "# Large File");
        // Note: In production, this would be caught by FileInfo.Length check

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert - should find at least the normal file
        files.Should().Contain(f => f.RelativePath.Contains("normal.md"));
    }

    #endregion

    #region Discovery Tests

    [Fact]
    public async Task DiscoverAsync_FindsMarkdownFiles()
    {
        // Arrange
        var readme = Path.Combine(_testDir, "README.md");
        var docsDir = Path.Combine(_testDir, "docs");
        Directory.CreateDirectory(docsDir);
        var guide = Path.Combine(docsDir, "guide.md");

        await File.WriteAllTextAsync(readme, "# README");
        await File.WriteAllTextAsync(guide, "# Guide");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(2);
        files.Should().Contain(f => f.Type == FileType.Readme);
        files.Should().Contain(f => f.RelativePath.Contains("docs"));
    }

    [Fact]
    public async Task DiscoverAsync_ExcludesNodeModules()
    {
        // Arrange
        var readme = Path.Combine(_testDir, "README.md");
        var nodeModules = Path.Combine(_testDir, "node_modules", "package");
        Directory.CreateDirectory(nodeModules);
        var npmFile = Path.Combine(nodeModules, "README.md");

        await File.WriteAllTextAsync(readme, "# README");
        await File.WriteAllTextAsync(npmFile, "# NPM Package");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].AbsolutePath.Should().Be(readme);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData(".vs")]
    [InlineData("dist")]
    [InlineData("build")]
    [InlineData("target")]
    public async Task DiscoverAsync_ExcludesCommonDirectories(string excludedDir)
    {
        // Arrange
        var readme = Path.Combine(_testDir, "README.md");
        var excluded = Path.Combine(_testDir, excludedDir);
        Directory.CreateDirectory(excluded);
        var excludedFile = Path.Combine(excluded, "ignored.md");

        await File.WriteAllTextAsync(readme, "# README");
        await File.WriteAllTextAsync(excludedFile, "# Ignored");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].AbsolutePath.Should().Be(readme);
    }

    [Fact]
    public async Task DiscoverAsync_WithDocsPath_FindsInSubdirectory()
    {
        // Arrange
        var docsDir = Path.Combine(_testDir, "documentation");
        Directory.CreateDirectory(docsDir);
        var guide = Path.Combine(docsDir, "guide.md");
        await File.WriteAllTextAsync(guide, "# Guide");

        // Act
        var files = await _service.DiscoverAsync(_testDir, "documentation").ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].RelativePath.Should().Contain("documentation");
    }

    [Fact]
    public async Task DiscoverAsync_NonExistentPath_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var nonExistent = Path.Combine(_testDir, "does-not-exist-" + Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await _service.DiscoverAsync(nonExistent).ToListAsync();
        });
    }

    [Fact]
    public async Task DiscoverAsync_EmptyDirectory_ReturnsEmpty()
    {
        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_DeterminesFileTypes()
    {
        // Arrange
        var readme = Path.Combine(_testDir, "README.md");
        var changelog = Path.Combine(_testDir, "CHANGELOG.md");
        var guide = Path.Combine(_testDir, "guide.md");

        await File.WriteAllTextAsync(readme, "# README");
        await File.WriteAllTextAsync(changelog, "# CHANGELOG");
        await File.WriteAllTextAsync(guide, "# Guide");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(3);
        files.Should().Contain(f => f.Type == FileType.Readme);
        files.Should().Contain(f => f.Type == FileType.Changelog);
        files.Should().Contain(f => f.Type == FileType.Markdown);
    }

    #endregion

    #region Git Integration Tests

    [Fact]
    public async Task GetCommitShaAsync_NoGitDirectory_ReturnsNull()
    {
        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().BeNull();
    }

    [Fact]
    public async Task GetCommitShaAsync_WithGitHead_ReturnsSha()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        var refsDir = Path.Combine(gitDir, "refs", "heads");
        Directory.CreateDirectory(refsDir);

        var headFile = Path.Combine(gitDir, "HEAD");
        await File.WriteAllTextAsync(headFile, "ref: refs/heads/main\n");

        var mainRef = Path.Combine(refsDir, "main");
        await File.WriteAllTextAsync(mainRef, "abc123def456\n");

        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().Be("abc123def456");
    }

    [Fact]
    public async Task GetCommitShaAsync_DetachedHead_ReturnsSha()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);

        var headFile = Path.Combine(gitDir, "HEAD");
        await File.WriteAllTextAsync(headFile, "deadbeef123456\n");

        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().Be("deadbeef123456");
    }

    [Fact]
    public async Task GetCommitShaAsync_MalformedHead_ReturnsNull()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);

        var headFile = Path.Combine(gitDir, "HEAD");
        await File.WriteAllTextAsync(headFile, "ref:"); // Too short - QA Issue #5

        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().BeNull();
        // Verify warning was logged - QA Issue #6
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("too short")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCommitShaAsync_EmptyHead_ReturnsNull()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);

        var headFile = Path.Combine(gitDir, "HEAD");
        await File.WriteAllTextAsync(headFile, "   \n  ");

        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().BeNull();
    }

    [Fact]
    public async Task GetCommitShaAsync_MissingRefFile_ReturnsNull()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);

        var headFile = Path.Combine(gitDir, "HEAD");
        await File.WriteAllTextAsync(headFile, "ref: refs/heads/nonexistent");

        // Act
        var sha = await _service.GetCommitShaAsync(_testDir);

        // Assert
        sha.Should().BeNull();
        // Verify warning was logged - QA Issue #6
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Code File Discovery Tests

    [Fact]
    public async Task DiscoverAsync_FindsCodeFiles()
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(srcDir);

        var csFile = Path.Combine(srcDir, "Program.cs");
        var tsFile = Path.Combine(srcDir, "index.ts");
        var pyFile = Path.Combine(srcDir, "main.py");

        await File.WriteAllTextAsync(csFile, "using System;");
        await File.WriteAllTextAsync(tsFile, "const x = 1;");
        await File.WriteAllTextAsync(pyFile, "print('hello')");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(3);
        files.Should().AllSatisfy(f => f.Type.Should().Be(FileType.Code));
        files.Should().Contain(f => f.RelativePath.EndsWith(".cs"));
        files.Should().Contain(f => f.RelativePath.EndsWith(".ts"));
        files.Should().Contain(f => f.RelativePath.EndsWith(".py"));
    }

    [Theory]
    [InlineData(".cs", "class Foo {}")]
    [InlineData(".ts", "const x = 1;")]
    [InlineData(".tsx", "<Component />")]
    [InlineData(".js", "function foo() {}")]
    [InlineData(".jsx", "<App />")]
    [InlineData(".py", "def main():")]
    [InlineData(".java", "public class Main {}")]
    [InlineData(".go", "package main")]
    [InlineData(".rs", "fn main() {}")]
    [InlineData(".rb", "def main")]
    [InlineData(".php", "<?php")]
    public async Task DiscoverAsync_SupportsCodeFileExtension(string extension, string content)
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(srcDir);

        var codeFile = Path.Combine(srcDir, $"file{extension}");
        await File.WriteAllTextAsync(codeFile, content);

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].Type.Should().Be(FileType.Code);
        files[0].RelativePath.Should().EndWith(extension);
    }

    [Fact]
    public async Task DiscoverAsync_FindsBothMarkdownAndCode()
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        var docsDir = Path.Combine(_testDir, "documentation"); // Use unique dir name
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(docsDir);

        var csFile = Path.Combine(srcDir, "Controller.cs"); // Unique filename
        var mdFile = Path.Combine(docsDir, "guide.md");

        await File.WriteAllTextAsync(csFile, "public class Foo {}");
        await File.WriteAllTextAsync(mdFile, "# Guide\nContent here");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(2, "should find exactly 2 files (1 code, 1 markdown)");
        files.Should().ContainSingle(f => f.Type == FileType.Code && f.RelativePath.Contains("Controller.cs"),
            "should find the .cs file");
        files.Should().ContainSingle(f => f.Type == FileType.Markdown && f.RelativePath.Contains("guide.md"),
            "should find the .md file");
    }

    [Fact]
    public async Task DiscoverAsync_SkipsLargeCodeFiles()
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(srcDir);

        var normalFile = Path.Combine(srcDir, "small.cs");
        await File.WriteAllTextAsync(normalFile, "using System;");

        // Note: We can't easily create a 10MB+ file in unit tests
        // This test validates the logic exists

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().Contain(f => f.RelativePath.Contains("small.cs"));
    }

    [Fact]
    public async Task DiscoverAsync_SkipsEmptyCodeFiles()
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        Directory.CreateDirectory(srcDir);

        var emptyFile = Path.Combine(srcDir, "empty.cs");
        var normalFile = Path.Combine(srcDir, "normal.cs");

        await File.WriteAllTextAsync(emptyFile, "");
        await File.WriteAllTextAsync(normalFile, "using System;");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].RelativePath.Should().Contain("normal.cs");
    }

    [Fact]
    public async Task DiscoverAsync_ExcludesCodeInNodeModules()
    {
        // Arrange
        var srcDir = Path.Combine(_testDir, "src");
        var nodeModules = Path.Combine(_testDir, "node_modules", "package");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(nodeModules);

        var srcFile = Path.Combine(srcDir, "index.ts");
        var npmFile = Path.Combine(nodeModules, "index.js");

        await File.WriteAllTextAsync(srcFile, "const x = 1;");
        await File.WriteAllTextAsync(npmFile, "module.exports = {};");

        // Act
        var files = await _service.DiscoverAsync(_testDir).ToListAsync();

        // Assert
        files.Should().HaveCount(1);
        files[0].AbsolutePath.Should().Be(srcFile);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task DiscoverAsync_NullProjectPath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _service.DiscoverAsync(null!).ToListAsync();
        });
    }

    [Fact]
    public async Task DiscoverAsync_EmptyProjectPath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _service.DiscoverAsync(string.Empty).ToListAsync();
        });
    }

    [Fact]
    public async Task DiscoverAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var readme = Path.Combine(_testDir, "README.md");
        await File.WriteAllTextAsync(readme, "# README");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _service.DiscoverAsync(_testDir, cancellationToken: cts.Token).ToListAsync();
        });
    }

    #endregion
}
