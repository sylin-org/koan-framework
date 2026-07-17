using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;

namespace GardenCoop.Models;

public sealed class Plot : Entity<Plot>
{
    [Required]
    public string Name { get; set; } = "";

    [Parent(typeof(Member))]
    public string? MemberId { get; set; }

    public string Notes { get; set; } = "";
}
