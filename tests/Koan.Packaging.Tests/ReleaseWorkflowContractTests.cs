using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseWorkflowContractTests
{
    private static readonly string[] ExpectedReleaseJobs =
    [
        "prepare_prior",
        "stage_prior",
        "promote_prior",
        "prove_current",
        "stage_current",
        "promote_current"
    ];

    [Fact]
    public void ReleaseRatchetBoundsSolutionTestTopology()
    {
        var ratchet = File.ReadAllText(Path.Combine(
            FindKoanRoot(),
            "scripts",
            "green-ratchet.ps1"));

        Assert.Contains("$testProjectConcurrency = 2", ratchet, StringComparison.Ordinal);
        Assert.Contains("$testHostHangTimeout = '5m'", ratchet, StringComparison.Ordinal);
        Assert.Contains("\"-m:$testProjectConcurrency\"", ratchet, StringComparison.Ordinal);
        Assert.Contains("'--blame-hang-timeout', $testHostHangTimeout", ratchet, StringComparison.Ordinal);
        Assert.Contains("'--blame-hang-dump-type', 'none'", ratchet, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedTestKitsAreExplicitlyNonRunnable()
    {
        var root = FindKoanRoot();
        var projects = Directory
            .EnumerateFiles(Path.Combine(root, "tests"), "*.TestKit.csproj", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var declarations = XDocument.Load(project)
                .Descendants("IsTestProject")
                .Select(element => element.Value.Trim())
                .ToArray();
            var relative = Path.GetRelativePath(root, project);

            Assert.True(
                declarations.Length == 1 &&
                string.Equals(declarations[0], "false", StringComparison.OrdinalIgnoreCase),
                $"Shared test kit '{relative}' must declare exactly one <IsTestProject>false</IsTestProject> boundary.");
        }
    }

    [Fact]
    public void ReleaseWorkflowUsesSixOrderedLeastPrivilegeBoundaries()
    {
        var workflow = ReadWorkflow();
        Assert.Equal(ExpectedReleaseJobs, JobNames(workflow));
        Assert.DoesNotContain("\nconcurrency:", workflow, StringComparison.Ordinal);

        var preparePrior = JobBlock(workflow, ExpectedReleaseJobs, "prepare_prior");
        var stagePrior = JobBlock(workflow, ExpectedReleaseJobs, "stage_prior");
        var promotePrior = JobBlock(workflow, ExpectedReleaseJobs, "promote_prior");
        var proveCurrent = JobBlock(workflow, ExpectedReleaseJobs, "prove_current");
        var stageCurrent = JobBlock(workflow, ExpectedReleaseJobs, "stage_current");
        var promoteCurrent = JobBlock(workflow, ExpectedReleaseJobs, "promote_current");

        AssertReadOnly(preparePrior);
        AssertReadOnly(proveCurrent);
        AssertContentsWriterWithoutOidc(stagePrior);
        AssertContentsWriterWithoutOidc(stageCurrent);
        AssertPublisher(promotePrior);
        AssertPublisher(promoteCurrent);

        Assert.Contains("needs: prepare_prior", stagePrior, StringComparison.Ordinal);
        Assert.Contains("- prepare_prior\n      - stage_prior", promotePrior, StringComparison.Ordinal);
        Assert.Contains("- prepare_prior\n      - promote_prior", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("- prepare_prior\n      - prove_current", stageCurrent, StringComparison.Ordinal);
        Assert.Contains("- prove_current\n      - stage_current", promoteCurrent, StringComparison.Ordinal);

        AssertOrdered(preparePrior,
            "- name: Wait for earlier dev release events",
            "- name: Resolve source and durable lineage",
            "- name: Inspect and prepare prior release wave",
            "- name: Preserve prior recovery handoff");
        AssertOrdered(proveCurrent,
            "- name: Compile current package lineage",
            "- name: Inspect compiled current release wave",
            "- name: Compile exact current release plan",
            "- name: Prove exact current version commit",
            "- name: Pack and prove current package closure",
            "- name: Prepare exact current release-wave escrow",
            "- name: Resolve current release disposition",
            "- name: Bundle exact current lineage candidate");
        AssertOrdered(stageCurrent,
            "- name: Persist exact version lineage",
            "- name: Stage exact current release wave");

        Assert.Contains("wave-inspect", preparePrior, StringComparison.Ordinal);
        Assert.Contains("materialize-lineage", preparePrior, StringComparison.Ordinal);
        Assert.Contains("$inspection.state -in @('missing', 'staging')", preparePrior, StringComparison.Ordinal);
        Assert.Contains("wave-bundle", preparePrior, StringComparison.Ordinal);
        Assert.Contains("wave-stage", stagePrior, StringComparison.Ordinal);
        Assert.Contains("wave-promote", promotePrior, StringComparison.Ordinal);
        Assert.Contains("wave-stage", stageCurrent, StringComparison.Ordinal);
        Assert.Contains("wave-promote", promoteCurrent, StringComparison.Ordinal);
        Assert.Contains("git bundle create", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("git -C $repository bundle verify", stageCurrent, StringComparison.Ordinal);
        Assert.Contains("git -C $repository push origin", stageCurrent, StringComparison.Ordinal);

        Assert.Contains("--previous-source", workflow, StringComparison.Ordinal);
        Assert.Contains("@('requested', 'queued', 'in_progress', 'waiting', 'pending')", workflow, StringComparison.Ordinal);
        Assert.Contains("green-ratchet.ps1", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("-PublicRelease", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093", workflow, StringComparison.Ordinal);
        Assert.Contains("uses: NuGet/login@ebc737b6fc418a6ca0073cf116ec8dc156d8b81e", workflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", workflow, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain --untracked-files=no", workflow, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(workflow, "uses: NuGet/login@", RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("uses: actions/checkout@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/setup-dotnet@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/upload-artifact@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: actions/download-artifact@v", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: NuGet/login@v", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverableHistoricalWaveIsProvenBeforeItIsPacked()
    {
        var preparePrior = JobBlock(ReadWorkflow(), ExpectedReleaseJobs, "prepare_prior");

        Assert.Contains("$inspection.state -in @('missing', 'staging')", preparePrior, StringComparison.Ordinal);
        AssertOrdered(preparePrior,
            "& dotnet $tool materialize-lineage",
            "$priorLineage = Get-Content artifacts/prior/release-lineage.json",
            "& dotnet $tool plan",
            "$releaseCount = @($manifest.packages).Count",
            "if ($releaseCount -gt 0)",
            "./scripts/green-ratchet.ps1",
            "-Base $priorLineage.previousSourceCommit",
            "git status --porcelain --untracked-files=no",
            "& dotnet $tool pack",
            "& dotnet $tool wave-bundle");
        Assert.Contains(
            "              if ($releaseCount -gt 0) {\n                ./scripts/green-ratchet.ps1",
            preparePrior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentWaveReplayOnlyCertifiesNonemptyMissingOrStagingEscrow()
    {
        var proveCurrent = JobBlock(ReadWorkflow(), ExpectedReleaseJobs, "prove_current");

        AssertOrdered(proveCurrent,
            "- name: Compile current package lineage",
            "- name: Inspect compiled current release wave",
            "- name: Compile exact current release plan",
            "- name: Prove exact current version commit",
            "- name: Resolve current release disposition");
        Assert.Contains("$needsPlan = $inspection.state -in @('missing', 'staging')", proveCurrent, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(
                proveCurrent,
                "if: steps.current.outputs.needs-plan == 'true'",
                RegexOptions.CultureInvariant)
            .Cast<Match>());
        Assert.Equal(3, Regex.Matches(
            proveCurrent,
            "if: steps.plan.outputs.has-packages == 'true'",
            RegexOptions.CultureInvariant).Count);
        Assert.Contains(
            "- name: Prove exact current version commit\n        if: steps.plan.outputs.has-packages == 'true'",
            proveCurrent,
            StringComparison.Ordinal);
        Assert.Contains("if ($env:CURRENT_STATE -eq 'published')", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("elseif ($env:CURRENT_STATE -eq 'prepared')", proveCurrent, StringComparison.Ordinal);
        Assert.Contains("$needsPromote = $true", proveCurrent, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstDurableLineageBootstrapsFromTheCurrentCoherentSource()
    {
        var proveCurrent = JobBlock(ReadWorkflow(), ExpectedReleaseJobs, "prove_current");

        AssertOrdered(proveCurrent,
            "$lineageBase = if ([string]::IsNullOrWhiteSpace($env:PREVIOUS_LINEAGE))",
            "$env:SOURCE_COMMIT",
            "else",
            "$env:PREVIOUS_SOURCE",
            "'--previous-source', $lineageBase");
        Assert.DoesNotContain("'--previous-source', $env:PREVIOUS_SOURCE", proveCurrent, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentLineagePersistenceIsAnExactReplaySafeCompareAndAdvance()
    {
        var stageCurrent = JobBlock(ReadWorkflow(), ExpectedReleaseJobs, "stage_current");

        AssertOrdered(stageCurrent,
            "$parents[1] -ne $lineage.previousVersionCommit",
            "$remoteHead = if ($remote.Count -eq 1)",
            "if ($remoteHead -eq $env:VERSION_COMMIT)",
            "Exact version lineage is already persisted; replay is a no-op.",
            "if ([string]::IsNullOrWhiteSpace($env:PREVIOUS_LINEAGE))",
            "git -C $repository push origin");
        Assert.Equal(2, Regex.Matches(
            stageCurrent,
            "if: needs.prove_current.outputs.needs-stage == 'true'",
            RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("if: needs.prove_current.outputs.has-packages == 'true'", stageCurrent, StringComparison.Ordinal);
        Assert.DoesNotContain("$parents[1] -ne $expectedParent", stageCurrent, StringComparison.Ordinal);
        Assert.DoesNotContain("$lineage.previousVersionCommit -ne $env:PREVIOUS_LINEAGE", stageCurrent, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotionProvesPreparedEscrowBeforeCredentialExchange()
    {
        var workflow = ReadWorkflow();
        var promotePrior = JobBlock(workflow, ExpectedReleaseJobs, "promote_prior");
        var promoteCurrent = JobBlock(workflow, ExpectedReleaseJobs, "promote_current");

        AssertPromotionGate(
            promotePrior,
            "- name: Require prepared prior escrow before credential exchange",
            "- name: Exchange GitHub identity for prior-wave NuGet credential",
            "- name: Promote exact prior release wave");
        AssertPromotionGate(
            promoteCurrent,
            "- name: Require prepared current escrow before credential exchange",
            "- name: Exchange GitHub identity for current-wave NuGet credential",
            "- name: Promote exact current release wave");

        Assert.Equal(3, Regex.Matches(
            promoteCurrent,
            "if: steps.gate.outputs.needs-promotion == 'true'",
            RegexOptions.CultureInvariant).Count);
        Assert.Equal(3, Regex.Matches(
            promotePrior,
            "if: needs.prepare_prior.outputs.prior-needs-promote == 'true'",
            RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("if: needs.prepare_prior.outputs.previous-lineage != ''", promotePrior, StringComparison.Ordinal);
        Assert.Contains("if: needs.prove_current.outputs.needs-promote == 'true'", promoteCurrent, StringComparison.Ordinal);
        Assert.DoesNotContain("if: needs.prove_current.outputs.has-packages == 'true'", promoteCurrent, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowDoesNotRetainTheSupersededMutablePublisher()
    {
        var workflow = ReadWorkflow();

        Assert.DoesNotContain("release-state.json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--clobber", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--verify-tag", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceShort", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("release/dev/", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project tools/Koan.Packaging", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet $tool publish", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Koan.Packaging.dll publish", workflow, StringComparison.Ordinal);

        // `dotnet publish` remains the read-only compiler handoff, not the removed package-publishing command.
        Assert.Contains(
            "dotnet publish tools/Koan.Packaging/Koan.Packaging.csproj",
            workflow,
            StringComparison.Ordinal);
    }

    private static void AssertReadOnly(string job)
    {
        Assert.Contains("permissions:\n      actions: read\n      contents: read", job, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", job, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", job, StringComparison.Ordinal);
    }

    private static void AssertContentsWriterWithoutOidc(string job)
    {
        Assert.Contains("permissions:\n      actions: read\n      contents: write", job, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", job, StringComparison.Ordinal);
        AssertPrivilegedJobDoesNotBuildOrTest(job);
    }

    private static void AssertPublisher(string job)
    {
        Assert.Contains(
            "permissions:\n      actions: read\n      contents: write\n      id-token: write",
            job,
            StringComparison.Ordinal);
        Assert.Contains("NuGet/login@ebc737b6fc418a6ca0073cf116ec8dc156d8b81e", job, StringComparison.Ordinal);
        AssertPrivilegedJobDoesNotBuildOrTest(job);
    }

    private static void AssertPrivilegedJobDoesNotBuildOrTest(string job)
    {
        Assert.DoesNotContain("actions/checkout@", job, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", job, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test", job, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet restore", job, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet run --project", job, StringComparison.Ordinal);
        Assert.DoesNotContain("green-ratchet.ps1", job, StringComparison.Ordinal);
        Assert.DoesNotContain(" --clean-room", job, StringComparison.Ordinal);
    }

    private static void AssertPromotionGate(
        string job,
        string gateStep,
        string credentialStep,
        string promotionStep)
    {
        AssertOrdered(job,
            gateStep,
            "& dotnet $tool wave-inspect",
            "if ($inspection.state -eq 'prepared')",
            "elseif ($inspection.state -eq 'published')",
            credentialStep,
            "uses: NuGet/login@ebc737b6fc418a6ca0073cf116ec8dc156d8b81e",
            promotionStep);
        Assert.Contains("must be prepared before credential exchange", job, StringComparison.Ordinal);
    }

    private static string ReadWorkflow() => File.ReadAllText(Path.Combine(
            FindKoanRoot(),
            ".github",
            "workflows",
            "release-on-dev.yml"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] JobNames(string workflow)
    {
        var jobs = workflow.IndexOf("\njobs:\n", StringComparison.Ordinal);
        Assert.True(jobs >= 0, "The release workflow must have a jobs section.");
        return Regex.Matches(
                workflow[(jobs + "\njobs:\n".Length)..],
                "(?m)^  ([a-z][a-z0-9_]*):$",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static string JobBlock(string workflow, string[] jobs, string name)
    {
        var index = Array.IndexOf(jobs, name);
        Assert.True(index >= 0, $"Release workflow has no '{name}' job.");
        var start = workflow.IndexOf($"\n  {name}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Release workflow has no '{name}' job block.");
        if (index == jobs.Length - 1) return workflow[start..];
        var end = workflow.IndexOf($"\n  {jobs[index + 1]}:\n", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, $"Release workflow job '{name}' has no stable boundary.");
        return workflow[start..end];
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
