using System.Runtime.CompilerServices;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseWorkflowContractTests
{
    [Fact]
    public void DurableLineagePrecedesEveryVersionSensitiveReleaseStep()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindKoanRoot(),
            ".github",
            "workflows",
            "release-on-dev.yml"));

        Assert.DoesNotContain("  build-verify:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  publish:\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\nconcurrency:", workflow, StringComparison.Ordinal);
        AssertOrdered(
            workflow,
            "- name: Wait for earlier dev release events",
            "- name: Compile durable package lineage",
            "- name: Persist exact version lineage",
            "- name: Build exact version commit",
            "- name: Test exact version commit",
            "- name: Compile exact release plan",
            "- name: Pack and prove package closure",
            "- name: Publish and reconcile exact artifacts");
        Assert.Contains("--previous-source", workflow, StringComparison.Ordinal);
        Assert.Contains("'HEAD:refs/heads/automation/package-lineage-dev'", workflow, StringComparison.Ordinal);
        Assert.Contains("--lineage artifacts/release/release-lineage.json", workflow, StringComparison.Ordinal);
        Assert.Contains("--target $manifest.versionCommit", workflow, StringComparison.Ordinal);
    }

    private static void AssertOrdered(string source, params string[] values)
    {
        var previous = -1;
        foreach (var value in values)
        {
            var current = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{value}' after the prior release step.");
            previous = current;
        }
    }

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
