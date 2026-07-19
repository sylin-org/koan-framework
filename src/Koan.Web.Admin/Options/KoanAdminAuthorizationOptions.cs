using System.ComponentModel.DataAnnotations;
using Koan.Web.Admin.Infrastructure;

namespace Koan.Web.Admin.Options;

public sealed class KoanAdminAuthorizationOptions
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Policy { get; set; } = KoanAdminDefaults.Policy;

    /// <summary>
    /// When true, Koan.Web.Admin creates a Development policy requiring an authenticated user when the application
    /// has not already registered the named policy.
    /// </summary>
    public bool AutoCreateDevelopmentPolicy { get; set; } = true;
}
