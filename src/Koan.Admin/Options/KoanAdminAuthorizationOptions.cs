using System.ComponentModel.DataAnnotations;
using Koan.Admin.Infrastructure;

namespace Koan.Admin.Options;

public sealed class KoanAdminAuthorizationOptions
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Policy { get; set; } = KoanAdminDefaults.Policy;

    public string[] AllowedNetworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true (default), Koan.Web.Admin auto-creates a permissive policy in Development if none is registered.
    /// </summary>
    public bool AutoCreateDevelopmentPolicy { get; set; } = true;
}
