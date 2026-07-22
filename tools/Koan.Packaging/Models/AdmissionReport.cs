namespace Koan.Packaging.Models;

internal sealed record AdmissionTestResult(string Name, string Outcome, string? Detail);

internal sealed record AdmissionReport(
    string CellId,
    string Project,
    string Filter,
    string Lane,
    string Phase,
    int DeadlineSeconds,
    string ReproductionCommand,
    string Verdict,
    int ProcessExitCode,
    bool TimedOut,
    bool KilledProcessTree,
    int Passed,
    int Failed,
    int Skipped,
    int Unknown,
    IReadOnlyList<AdmissionTestResult> Results,
    IReadOnlyList<string> Reasons);
