using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Cqrs;

public sealed class CqrsEntityRoute
{
    [Required]
    public CqrsEndpoint Write { get; set; } = new();
    [Required]
    public CqrsEndpoint Read { get; set; } = new();
}