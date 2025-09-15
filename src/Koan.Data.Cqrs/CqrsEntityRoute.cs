using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Cqrs;

public sealed class CqrsEntityRoute
{
    [Required]
    public CqrsEndpoint Write { get; set; } = new();
    [Required]
    public CqrsEndpoint Read { get; set; } = new();
}