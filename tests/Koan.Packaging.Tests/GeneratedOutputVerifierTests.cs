using System.Runtime.CompilerServices;
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
    public void AcceptsAnExactGeneratedOutput()
    {
        Seed("docs/reference/surface.json", "current" + Environment.NewLine);

        new GeneratedOutputVerifier(root).RequireMatch("docs/reference/surface.json", "current");
    }

    [Fact]
    public void RejectsAStaleGeneratedOutputWithTheCanonicalCorrection()
    {
        Seed("docs/reference/surface.md", "stale" + Environment.NewLine);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new GeneratedOutputVerifier(root).RequireMatch("docs/reference/surface.md", "current"));

        Assert.Contains("docs/reference/surface.md", error.Message, StringComparison.Ordinal);
        Assert.Contains("stale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product-surface --output", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPullRequestGateChecksBothOutputsBeforeTheGreenRatchet()
    {
        var workflow = File.ReadAllText(Path.Combine(FindKoanRoot(), ".github", "workflows", "pr-gate.yml"));
        var surface = workflow.IndexOf(
            "dotnet run --project tools/Koan.Packaging -- product-surface --check",
            StringComparison.Ordinal);
        var baselines = workflow.IndexOf(
            "dotnet run --project tools/Koan.Packaging -- api-baselines",
            StringComparison.Ordinal);
        var ratchet = workflow.IndexOf("./scripts/green-ratchet.ps1", StringComparison.Ordinal);
        var deterministicAdmissions = workflow.IndexOf(
            "Enforce exact deterministic claim admissions",
            StringComparison.Ordinal);

        Assert.True(surface >= 0, "the main PR gate must execute the real product-surface compiler");
        Assert.True(baselines > surface, "the supported API-baseline guard must follow valid product truth");
        Assert.True(ratchet > baselines, "both deterministic product guards must pass before the green ratchet");
        Assert.True(deterministicAdmissions > ratchet,
            "exact deterministic claim cells must validate the built merge candidate after the green ratchet");
        Assert.Contains("$claims.admission", workflow, StringComparison.Ordinal);
        Assert.Contains("Where-Object lane -eq 'deterministic'", workflow, StringComparison.Ordinal);

        var ratchetScript = File.ReadAllText(Path.Combine(FindKoanRoot(), "scripts", "green-ratchet.ps1"));
        Assert.Contains("KoanLane!=native", ratchetScript, StringComparison.Ordinal);
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

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
