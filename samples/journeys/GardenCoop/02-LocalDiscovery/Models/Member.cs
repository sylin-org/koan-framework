using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using System.ComponentModel.DataAnnotations;

namespace GardenCoop.Models;

public sealed class Member : Entity<Member>
{
    [Required]
    public string DisplayName { get; set; } = "";
}
