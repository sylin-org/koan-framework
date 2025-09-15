using Koan.Flow.Model;

namespace S8.Location.Core.Models;

public class LocationResolvedEvent : FlowValueObject<LocationResolvedEvent>
{
    public string LocationId { get; set; } = "";
    public string CanonicalId { get; set; } = "";
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
}

public class LocationErrorEvent : FlowValueObject<LocationErrorEvent>
{
    public string LocationId { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string Address { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}