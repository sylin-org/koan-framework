using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;

namespace g1c1.GardenCoop.Models;

// Plot = "bed" in garden terms - inheriting from Entity<Plot> gives me .Save(), .Get(), .Query() for free
public class Plot : Entity<Plot>
{
    [Required]
    public string Name { get; set; } = string.Empty;

    // nullable MemberId = plots can exist without stewards assigned yet
    [Parent(typeof(Member))]  // tried this attribute - it wires up the relationship for me
    public string? MemberId { get; set; }

    // field notes stay with the bed, not the person - makes sense for perennial info
    public string Notes { get; set; } = string.Empty;
}
