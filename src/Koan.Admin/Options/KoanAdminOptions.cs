using System.ComponentModel.DataAnnotations;
using Koan.Admin.Infrastructure;

namespace Koan.Admin.Options;

public sealed class KoanAdminOptions
{
    public bool Enabled { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool EnableConsoleUi { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool EnableWeb { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool EnableLaunchKit { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool AllowInProduction { get; set; }
        = false;
    public bool AllowDotPrefixInProduction { get; set; }
        = false;

    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string PathPrefix { get; set; } = KoanAdminDefaults.Prefix;

    public bool ExposeManifest { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool DestructiveOps { get; set; }
        = false;

    [Required]
    public KoanAdminAuthorizationOptions Authorization { get; set; } = new();

    [Required]
    public KoanAdminLoggingOptions Logging { get; set; } = new();

    [Required]
    public KoanAdminGenerateOptions Generate { get; set; } = new();
}
