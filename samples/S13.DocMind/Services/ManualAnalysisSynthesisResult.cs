using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed record ManualAnalysisSynthesisResult(
    ManualAnalysisSynthesis Synthesis,
    ManualAnalysisRun RunTelemetry);
