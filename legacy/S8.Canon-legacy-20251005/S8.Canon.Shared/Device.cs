using System.ComponentModel.DataAnnotations;
using Koan.Canon.Attributes;
using Koan.Canon.Model;

namespace S8.Canon.Shared;

public sealed class Device : CanonEntity<Device>
{
    public string Inventory { get; set; } = default!;

    [AggregationKey]
    public string Serial { get; set; } = default!;
    public string Manufacturer { get; set; } = default!;
    public string Model { get; set; } = default!;
    public string Kind { get; set; } = default!;
    public string Code { get; set; } = default!;
}

