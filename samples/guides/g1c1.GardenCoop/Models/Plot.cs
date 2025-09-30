using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using System.ComponentModel.DataAnnotations;

namespace g1c1.GardenCoop.Models;

[DataAdapter("sqlite")]
public class Plot : Entity<Plot>
{
    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    [Parent(typeof(Member))]
    public string MemberId { get; set; } = string.Empty;
}
