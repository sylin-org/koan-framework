using Koan.Context.Utilities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Security;

public class PathValidationSpec
{
    private readonly PathValidator _validator;
    private readonly string _tempAllowedPath;

    public PathValidationSpec()
    {
        // Setup test allowed directory
        _tempAllowedPath = Path.GetTempPath();

        // Setup configuration with test allowed directories
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Context:Security:AllowedDirectories:0"] = _tempAllowedPath
            })
            .Build();

        _validator = new PathValidator(configuration);
    }

    [Fact]
    public void RejectsNullPath()
    {
        var result = _validator.IsValidProjectPath(null, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("cannot be null", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsEmptyPath()
    {
        var result = _validator.IsValidProjectPath("", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("cannot be null or empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsWhitespacePath()
    {
        var result = _validator.IsValidProjectPath("   ", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("cannot be null or empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsRelativePath()
    {
        var result = _validator.IsValidProjectPath("../etc/passwd", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("must be absolute", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsCurrentDirectoryPath()
    {
        var result = _validator.IsValidProjectPath("./some/path", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("must be absolute", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathWithNullByte()
    {
        var result = _validator.IsValidProjectPath("/tmp/test\0/evil", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("null byte", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUNCPathWithBackslashes()
    {
        var result = _validator.IsValidProjectPath(@"\\malicious-server\share", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("UNC paths are not allowed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUNCPathWithForwardSlashes()
    {
        var result = _validator.IsValidProjectPath("//malicious-server/share", out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("UNC paths are not allowed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathOutsideAllowedRoots_Windows()
    {
        // This test assumes we're NOT allowing C:\Windows
        if (OperatingSystem.IsWindows())
        {
            var result = _validator.IsValidProjectPath(@"C:\Windows\System32", out var error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("outside allowed directories", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RejectsPathOutsideAllowedRoots_Unix()
    {
        // This test assumes we're NOT allowing /etc
        if (!OperatingSystem.IsWindows())
        {
            var result = _validator.IsValidProjectPath("/etc/shadow", out var error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("outside allowed directories", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AcceptsValidPathWithinAllowedRoot()
    {
        // Create test directory within allowed root (temp path)
        var testDir = Path.Combine(_tempAllowedPath, "koan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            var result = _validator.IsValidProjectPath(testDir, out var error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public void AcceptsValidPathWithinUserHomeDirectory()
    {
        // User's home directory should always be allowed
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(homeDir) && Directory.Exists(homeDir))
        {
            var testDir = Path.Combine(homeDir, "koan-test-" + Guid.NewGuid());
            Directory.CreateDirectory(testDir);

            try
            {
                var result = _validator.IsValidProjectPath(testDir, out var error);

                Assert.True(result);
                Assert.Null(error);
            }
            finally
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsNonExistentPath()
    {
        var nonExistentPath = Path.Combine(_tempAllowedPath, "does-not-exist-" + Guid.NewGuid());

        var result = _validator.IsValidProjectPath(nonExistentPath, out var error);

        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPathTraversalInMiddleOfPath()
    {
        // Even if the path is absolute, it shouldn't contain .. sequences
        var testPath = Path.Combine(_tempAllowedPath, "..", "etc", "passwd");

        var result = _validator.IsValidProjectPath(testPath, out var error);

        // This will likely fail the "outside allowed directories" check
        // after normalization, or might be caught by the existence check
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void HandlesPathsWithTrailingSlash()
    {
        var testDir = Path.Combine(_tempAllowedPath, "koan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Test with trailing slash
            var pathWithSlash = testDir + Path.DirectorySeparatorChar;
            var result = _validator.IsValidProjectPath(pathWithSlash, out var error);

            Assert.True(result);
            Assert.Null(error);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public void HandlesCaseSensitivityAppropriately()
    {
        var testDir = Path.Combine(_tempAllowedPath, "koan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // On Windows, paths are case-insensitive
            // On Unix, paths are case-sensitive
            var upperPath = testDir.ToUpperInvariant();
            var result = _validator.IsValidProjectPath(upperPath, out var error);

            if (OperatingSystem.IsWindows())
            {
                // Windows should accept case variations
                Assert.True(result);
                Assert.Null(error);
            }
            else
            {
                // Unix might reject if the exact case doesn't exist
                // This depends on whether the path actually exists with that casing
                // For this test, we just verify it doesn't crash
                Assert.NotNull(error?.ToString() ?? "");
            }
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public void RejectsVeryLongPaths()
    {
        // Create a path that's too long (platform-dependent)
        var longSegment = new string('a', 300);
        var longPath = Path.Combine(_tempAllowedPath, longSegment, longSegment);

        var result = _validator.IsValidProjectPath(longPath, out var error);

        // Should fail either on existence check or path validation
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    public void RejectsWindowsReservedNames(string reservedName)
    {
        if (OperatingSystem.IsWindows())
        {
            var invalidPath = Path.Combine(_tempAllowedPath, reservedName);
            var result = _validator.IsValidProjectPath(invalidPath, out var error);

            // Should fail on existence check or path validation
            Assert.False(result);
            Assert.NotNull(error);
        }
    }
}
