using System;

namespace S12.MedTrials.Models;

public sealed class DocumentDiagnostic
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Info;
}
