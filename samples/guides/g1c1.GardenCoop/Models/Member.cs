using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using System.ComponentModel.DataAnnotations;

namespace g1c1.GardenCoop.Models;

[DataAdapter("sqlite")]
public class Member : Entity<Member>
{
    [Required]
    [MaxLength(64)]
    public string DisplayName { get; set; } = string.Empty;
}
