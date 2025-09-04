using System;
using System.Collections.Generic;

namespace S8.Flow.Shared;

public sealed class SensorLatestReading : Sora.Data.Abstractions.IEntity<string>
{
    public string Id { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public Dictionary<string, object> View { get; set; } = new();
}
