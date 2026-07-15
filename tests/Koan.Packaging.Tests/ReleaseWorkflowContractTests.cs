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
            "- name: Validate exact version commit",
            "- name: Compile exact release plan",
            "- name: Pack and prove package closure",
            "- name: Publish and reconcile exact artifacts");
        Assert.Contains("--previous-source", workflow, StringComparison.Ordinal);
        Assert.Contains("@('requested', 'queued', 'in_progress', 'waiting', 'pending')", workflow, StringComparison.Ordinal);
        Assert.Contains("'HEAD:refs/heads/automation/package-lineage-dev'", workflow, StringComparison.Ordinal);
        Assert.Contains("--lineage artifacts/release/release-lineage.json", workflow, StringComparison.Ordinal);
        Assert.Contains("green-ratchet.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-PublicRelease", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: NuGet/login@ebc737b6fc418a6ca0073cf116ec8dc156d8b81e", workflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", workflow, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain --untracked-files=no", workflow, StringComparison.Ordinal);
        Assert.Contains("git push origin \"$($manifest.versionCommit):$tagRef\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-tag", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--target $manifest.versionCommit", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/checkout@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/setup-dotnet@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/upload-artifact@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: NuGet/login@v", workflow, StringComparison.Ordinal);
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
