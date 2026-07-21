using System.ComponentModel.DataAnnotations;
using Koan.Web.Admin.Infrastructure;

namespace Koan.Web.Admin.Options;

public sealed class KoanAdminOptions
{
    /// <summary>Enables the Development-only dashboard. Non-Development hosts remain inactive.</summary>
    public bool Enabled { get; set; } = true;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string PathPrefix { get; set; } = KoanAdminDefaults.Prefix;

    [Required]
    public KoanAdminAuthorizationOptions Authorization { get; set; } = new();
}
