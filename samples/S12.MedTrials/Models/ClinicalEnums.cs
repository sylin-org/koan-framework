namespace S12.MedTrials.Models;

public enum VisitStatus
{
    Scheduled,
    Completed,
    Proposed,
    Cancelled
}

public enum VisitType
{
    Screening,
    Baseline,
    Treatment,
    FollowUp,
    Telehealth,
    SafetyCheck
}

public enum AdverseEventSeverity
{
    Mild,
    Moderate,
    Severe,
    LifeThreatening
}

public enum AdverseEventStatus
{
    Open,
    Investigating,
    Closed,
    Escalated
}

public enum ProtocolVectorState
{
    Pending,
    Indexed,
    Degraded
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Critical
}
