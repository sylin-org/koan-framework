using System.Diagnostics;
using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class AdmissionResultValidatorTests
{
    [Fact]
    public void Exact_passed_result_is_admitted()
    {
        using var fixture = new TrxFixture(("lifecycle_restores_ambient", "Passed", null));

        var report = fixture.Validate(new ProcessResult(0, string.Empty, string.Empty));

        Assert.Equal("passed", report.Verdict);
        Assert.Equal(1, report.Passed);
        Assert.Empty(report.Reasons);
    }

    [Theory]
    [InlineData("Failed", 1, 0, 0, "failed result")]
    [InlineData("NotExecuted", 0, 1, 0, "skipped/not-executed")]
    [InlineData("Skipped", 0, 1, 0, "skipped/not-executed")]
    [InlineData("InProgress", 0, 0, 1, "unknown-outcome")]
    public void Every_nonpassed_outcome_fails_closed(
        string outcome,
        int failed,
        int skipped,
        int unknown,
        string reason)
    {
        using var fixture = new TrxFixture(("required_lifecycle_cell", outcome, "phase detail"));

        var report = fixture.Validate(new ProcessResult(0, string.Empty, string.Empty));

        Assert.Equal("failed", report.Verdict);
        Assert.Equal(failed, report.Failed);
        Assert.Equal(skipped, report.Skipped);
        Assert.Equal(unknown, report.Unknown);
        Assert.Contains(report.Reasons, value => value.Contains(reason, StringComparison.Ordinal));
        Assert.Equal("phase detail", Assert.Single(report.Results).Detail);
    }

    [Fact]
    public void Present_passed_trx_cannot_mask_nonzero_process_exit()
    {
        using var fixture = new TrxFixture(("teardown_cell", "Passed", null));

        var report = fixture.Validate(new ProcessResult(17, "", "teardown failed"));

        Assert.Equal("failed", report.Verdict);
        Assert.Contains(report.Reasons, value => value.Contains("exited with code 17", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_result_file_fails_closed()
    {
        using var fixture = new TrxFixture();
        File.Delete(fixture.Path);

        var report = fixture.Validate(new ProcessResult(0, string.Empty, string.Empty));

        Assert.Equal("failed", report.Verdict);
        Assert.Contains(report.Reasons, value => value.Contains("produced no TRX", StringComparison.Ordinal));
        Assert.Contains(report.Reasons, value => value.Contains("zero test results", StringComparison.Ordinal));
    }

    [Fact]
    public void Zero_results_fails_closed()
    {
        using var fixture = new TrxFixture();

        var report = fixture.Validate(new ProcessResult(0, string.Empty, string.Empty));

        Assert.Equal("failed", report.Verdict);
        Assert.Contains(report.Reasons, value => value.Contains("zero test results", StringComparison.Ordinal));
    }

    [Fact]
    public void Timeout_reports_phase_deadline_and_owned_tree_cleanup()
    {
        using var fixture = new TrxFixture(("setup_reaches_readiness", "Passed", null));

        var report = fixture.Validate(new ProcessResult(-1, "", "", TimedOut: true, KilledProcessTree: true));

        Assert.Equal("failed", report.Verdict);
        Assert.Contains(report.Reasons, value => value.Contains("phase 'lifecycle'", StringComparison.Ordinal));
        Assert.Contains(report.Reasons, value => value.Contains("3s deadline", StringComparison.Ordinal));
        Assert.Contains(report.Reasons, value => value.Contains("owned process tree was terminated", StringComparison.Ordinal));
    }

    private sealed class TrxFixture : IDisposable
    {
        public TrxFixture(params (string Name, string Outcome, string? Detail)[] results)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"koan-admission-{Guid.NewGuid():N}.trx");
            var definitions = string.Join(
                "",
                results.Select((result, index) =>
                    $"<UnitTest id=\"{index}\"><TestMethod name=\"{result.Name}\" /></UnitTest>"));
            var resultElements = string.Join(
                "",
                results.Select((result, index) =>
                    $"<UnitTestResult testId=\"{index}\" testName=\"{result.Name}\" outcome=\"{result.Outcome}\">" +
                    (result.Detail is null ? "" : $"<Output><ErrorInfo><Message>{result.Detail}</Message></ErrorInfo></Output>") +
                    "</UnitTestResult>"));
            File.WriteAllText(Path, $"<TestRun><TestDefinitions>{definitions}</TestDefinitions><Results>{resultElements}</Results></TestRun>");
        }

        public string Path { get; }

        public Koan.Packaging.Models.AdmissionReport Validate(ProcessResult process) =>
            new AdmissionResultValidator().Validate(
                "claim:lifecycle",
                "tests/Owner.Tests.csproj",
                "FullyQualifiedName=Owner.Lifecycle",
                "deterministic",
                "lifecycle",
                3,
                "dotnet test tests/Owner.Tests.csproj",
                process,
                Path);

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}

public sealed class ProcessRunnerDeadlineTests
{
    [Fact]
    public async Task Timeout_kills_only_the_owned_process_tree()
    {
        var pidFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"koan-admission-child-{Guid.NewGuid():N}.pid");
        var escapedPidFile = pidFile.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$child = Start-Process -FilePath 'pwsh' " +
            "-ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 30') -PassThru; " +
            $"Set-Content -LiteralPath '{escapedPidFile}' -Value $child.Id; " +
            "Start-Sleep -Seconds 30";

        try
        {
            var result = await new ProcessRunner().RunAsync(
                "pwsh",
                ["-NoProfile", "-Command", script],
                Environment.CurrentDirectory,
                CancellationToken.None,
                timeout: TimeSpan.FromSeconds(1));

            Assert.True(result.TimedOut);
            Assert.True(result.KilledProcessTree);
            Assert.True(File.Exists(pidFile), "The owned child process should have started before the deadline.");
            var childId = int.Parse(await File.ReadAllTextAsync(pidFile));
            AssertOwnedChildExited(childId);
        }
        finally
        {
            if (File.Exists(pidFile)) File.Delete(pidFile);
        }
    }

    private static void AssertOwnedChildExited(int processId)
    {
        try
        {
            using var child = Process.GetProcessById(processId);
            Assert.True(child.WaitForExit(5000), $"Owned child process {processId} survived the tree kill.");
        }
        catch (ArgumentException)
        {
            // The process is already absent, which is the desired state.
        }
    }
}

public sealed class AdmissionScriptContractTests
{
    [Fact]
    public void Forge_uses_bounded_admission_and_requires_record_streaming()
    {
        var script = File.ReadAllText(RepositoryPath("scripts", "forge-verify.ps1"));

        Assert.Contains("'admission'", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateRange(1, 3600)][int]$DeadlineSeconds", script, StringComparison.Ordinal);
        Assert.Contains("@('Declares', 'Streaming', 'Shared', 'Container', 'Database')", script, StringComparison.Ordinal);
        Assert.Contains("[int]$admission.processExitCode -ne 0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$dotnetArgs = @('test'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_emits_trx_and_validates_every_selected_result()
    {
        var script = File.ReadAllText(RepositoryPath("scripts", "test-bootstrap.ps1"));

        Assert.Contains("\"-failSkips\", \"-trx\", $trxPath", script, StringComparison.Ordinal);
        Assert.Contains("\"admission-results\"", script, StringComparison.Ordinal);
        Assert.Contains("finally {", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $resultsRoot", script, StringComparison.Ordinal);
    }

    private static string RepositoryPath(string first, string second, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..")), first, second);
}
