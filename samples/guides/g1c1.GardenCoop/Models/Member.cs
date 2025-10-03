using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using System.ComponentModel.DataAnnotations;

namespace g1c1.GardenCoop.Models;

// Member = someone in the co-op who can be assigned to steward plots
public class Member : Entity<Member>
{
    [Required]
    public string DisplayName { get; set; } = string.Empty;
}
