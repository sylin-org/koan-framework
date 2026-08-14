using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class GeneratedOutputVerifierTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "koan-generated-output-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AcceptsAnExactGeneratedMarkdownProjection()
    {
        Seed("docs/reference/surface.md", "current" + Environment.NewLine);

        new GeneratedOutputVerifier(root).RequireMatch("docs/reference/surface.md", "current");
    }

    [Fact]
    public void RejectsAStaleGeneratedOutputWithTheCanonicalCorrection()
    {
        Seed("docs/reference/surface.md", "stale" + Environment.NewLine);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new GeneratedOutputVerifier(root).RequireMatch("docs/reference/surface.md", "current"));

        Assert.Contains("docs/reference/surface.md", error.Message, StringComparison.Ordinal);
        Assert.Contains("stale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product-surface --markdown", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Seed(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
