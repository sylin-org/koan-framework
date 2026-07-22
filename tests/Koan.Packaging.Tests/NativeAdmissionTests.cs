using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class NativeAdmissionPlannerTests
{
    [Fact]
    public void Unaffected_candidate_is_machine_derived_not_applicable()
    {
        var plan = Planner().Create("base", "candidate", ["docs/unrelated.md"], [Project("A")], Surface(Claim("claim-a", "A", Native("a:native")), Package("A")));

        Assert.Equal("not-applicable", plan.Applicability);
        Assert.Empty(plan.AffectedClaims);
        Assert.Empty(plan.Cells);
        Assert.Equal("candidate", plan.CandidateCommit);
    }

    [Fact]
    public void Changed_owner_requires_its_native_cell()
    {
        var plan = Planner().Create("base", "candidate", ["src/A/Runtime.cs"], [Project("A")], Surface(Claim("claim-a", "A", Native("a:native")), Package("A")));

        Assert.Equal("required", plan.Applicability);
        Assert.Equal(["claim-a"], plan.AffectedClaims);
        Assert.Equal("a:native", Assert.Single(plan.Cells).Id);
    }

    [Fact]
    public void Changed_dependency_conservatively_affects_downstream_claim()
    {
        var surface = Surface(
            Claim("claim-b", "B", Native("b:native")),
            Package("A"),
            Package("B", "A"));

        var plan = Planner().Create(
            "base",
            "candidate",
            ["src/A/Runtime.cs"],
            [Project("A"), Project("B")],
            surface);

        Assert.Equal("required", plan.Applicability);
        Assert.Equal(["claim-b"], plan.AffectedClaims);
    }

    [Theory]
    [InlineData("product/claims.json")]
    [InlineData("Directory.Build.targets")]
    [InlineData("tools/Koan.Packaging/Services/AdmissionRunner.cs")]
    [InlineData("tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/Shared.cs")]
    public void Shared_truth_or_runner_change_affects_declared_native_cells(string changedPath)
    {
        var plan = Planner().Create("base", "candidate", [changedPath], [Project("A")], Surface(Claim("claim-a", "A", Native("a:native")), Package("A")));

        Assert.Equal("required", plan.Applicability);
        Assert.Equal("a:native", Assert.Single(plan.Cells).Id);
    }

    [Fact]
    public void Affected_deterministic_only_claim_remains_native_not_applicable()
    {
        var plan = Planner().Create(
            "base",
            "candidate",
            ["src/A/Runtime.cs"],
            [Project("A")],
            Surface(Claim("claim-a", "A", Native("a:deterministic") with { Lane = "deterministic" }), Package("A")));

        Assert.Equal("not-applicable", plan.Applicability);
        Assert.Equal(["claim-a"], plan.AffectedClaims);
        Assert.Empty(plan.Cells);
    }

    [Fact]
    public void Cell_project_change_affects_its_claim()
    {
        var plan = Planner().Create(
            "base",
            "candidate",
            ["tests/A/NativeSpec.cs"],
            [Project("A")],
            Surface(Claim("claim-a", "A", Native("a:native")), Package("A")));

        Assert.Equal("required", plan.Applicability);
    }

    private static NativeAdmissionPlanner Planner() => new();

    private static ProductSurface Surface(ProductClaim claim, params ProductPackage[] packages) => new()
    {
        Source = "product/claims.json",
        Claims = [claim],
        Packages = packages.ToList()
    };

    private static ProductClaim Claim(string id, string package, AdmissionCell cell) => new(
        id,
        id,
        "Outcome",
        "verified",
        [package],
        ["docs/claim.md"],
        ["tests/evidence"],
        [cell]);

    private static AdmissionCell Native(string id) => new(
        id,
        "tests/A/A.Tests.csproj",
        "FullyQualifiedName=A.NativeSpec",
        "native",
        "lifecycle",
        120);

    private static ProductPackage Package(string id, params string[] dependencies) => new(
        id, "0.20", "library", "Description", ["net10.0"], dependencies, "README.md", true, "TECHNICAL.md", []);

    private static PackageProject Project(string id) => new(
        $"src/{id}/{id}.csproj",
        $"src/{id}",
        id,
        "Dependency",
        ["net10.0"],
        false,
        false,
        true,
        false,
        true,
        "README.md",
        true,
        "TECHNICAL.md",
        "Description",
        "koan",
        []);
}

public sealed class NativeCandidateGuardTests
{
    [Fact]
    public void Accepts_exact_checked_out_candidate_with_ancestor_base() =>
        NativeCandidateGuard.RequireExact("base", "candidate", "CANDIDATE", baseIsAncestor: true, worktreeIsClean: true);

    [Fact]
    public void Rejects_foreign_result_candidate()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            NativeCandidateGuard.RequireExact("base", "foreign", "candidate", baseIsAncestor: true, worktreeIsClean: true));

        Assert.Contains("checked-out HEAD", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unrelated_base()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            NativeCandidateGuard.RequireExact("base", "candidate", "candidate", baseIsAncestor: false, worktreeIsClean: true));

        Assert.Contains("not an ancestor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_dirty_candidate_checkout()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            NativeCandidateGuard.RequireExact("base", "candidate", "candidate", baseIsAncestor: true, worktreeIsClean: false));

        Assert.Contains("not an exact checkout", error.Message, StringComparison.Ordinal);
    }
}

public sealed class NativeAdmissionExecutorTests
{
    [Fact]
    public async Task Not_applicable_plan_runs_no_cell_and_passes_as_na()
    {
        var calls = 0;
        var executor = new NativeAdmissionExecutor((_, _, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("must not run");
        });

        var report = await executor.RunAsync(Plan([]), "Release", false, CancellationToken.None);

        Assert.Equal("not-applicable", report.Verdict);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Every_required_cell_must_return_passed()
    {
        var cells = new[] { Cell("one"), Cell("two") };
        var executor = new NativeAdmissionExecutor((cell, _, _, _) => Task.FromResult(
            Result(cell, cell.Id == "one" ? "passed" : "failed")));

        var report = await executor.RunAsync(Plan(cells), "Release", false, CancellationToken.None);

        Assert.Equal("failed", report.Verdict);
        Assert.Equal(2, report.Results.Count);
        Assert.Contains(report.Reasons, reason => reason.Contains("two", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_required_cell_fails_closed_on_exact_candidate()
    {
        var executor = new NativeAdmissionExecutor((cell, _, _, _) =>
            throw new InvalidOperationException($"no report for {cell.Id}"));

        var report = await executor.RunAsync(Plan([Cell("missing")]), "Release", false, CancellationToken.None);

        Assert.Equal("failed", report.Verdict);
        Assert.Empty(report.Results);
        Assert.Contains(report.Reasons, reason => reason.Contains("candidate-sha", StringComparison.Ordinal));
    }

    private static NativeAdmissionPlan Plan(IReadOnlyList<NativeAdmissionCell> cells) => new(
        "base-sha",
        "candidate-sha",
        cells.Count == 0 ? "not-applicable" : "required",
        ["src/A/Runtime.cs"],
        cells.Count == 0 ? [] : ["claim-a"],
        cells,
        "test plan");

    private static NativeAdmissionCell Cell(string id) => new(
        "claim-a", id, "tests/A/A.Tests.csproj", "FullyQualifiedName=A.Spec", "native", "lifecycle", 30);

    private static AdmissionReport Result(NativeAdmissionCell cell, string verdict) => new(
        cell.Id,
        cell.Project,
        cell.Filter,
        cell.Lane,
        cell.Phase,
        cell.DeadlineSeconds,
        "reproduce",
        verdict,
        verdict == "passed" ? 0 : 1,
        false,
        false,
        verdict == "passed" ? 1 : 0,
        verdict == "passed" ? 0 : 1,
        0,
        0,
        [new AdmissionTestResult("Spec", verdict == "passed" ? "Passed" : "Failed", null)],
        verdict == "passed" ? [] : ["failed"]);
}

public sealed class NativeAdmissionWorkflowContractTests
{
    [Fact]
    public void Main_pr_check_is_always_emitted_and_binds_github_merge_sha()
    {
        var workflow = File.ReadAllText(RepositoryPath(".github", "workflows", "canary-nightly.yml"));

        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("branches: [main]", workflow, StringComparison.Ordinal);
        Assert.Contains("--base \"${{ github.event.pull_request.base.sha }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--candidate \"${{ github.sha }}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nuget push", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryPath(
        string first,
        string second,
        string third,
        [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..")), first, second, third);
}
