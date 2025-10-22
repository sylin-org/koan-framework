using System;
using Koan.Jobs.Model;

namespace Koan.Jobs.Events;

public sealed record JobNotification(
    string JobId,
    JobStatus Status,
    string? CorrelationId,
    string EventType,
    DateTimeOffset Timestamp,
    string? Error);

public sealed record JobProgressNotification(
    string JobId,
    JobStatus Status,
    double Progress,
    string? Message,
    DateTimeOffset Timestamp);
