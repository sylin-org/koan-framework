using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Cqrs;

public sealed class CqrsProfile
{
    [Required]
    public Dictionary<string, CqrsEntityRoute> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CqrsMessaging Messaging { get; set; } = new();
}