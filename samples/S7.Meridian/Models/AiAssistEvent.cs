using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class AiAssistEvent : Entity<AiAssistEvent>
{
    public string EntityType { get; set; } = string.Empty;
    public string? SuggestedEntityName { get; set; }
        = null;
    public string RequestSummary { get; set; } = string.Empty;
    public string ResponseSummary { get; set; } = string.Empty;
    public string? Model { get; set; }
        = null;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
