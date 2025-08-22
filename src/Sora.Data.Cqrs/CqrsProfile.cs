using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Cqrs;

public sealed class CqrsProfile
{
    [Required]
    public Dictionary<string, CqrsEntityRoute> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CqrsMessaging Messaging { get; set; } = new();
}