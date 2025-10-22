using System;
using System.ComponentModel.DataAnnotations;

namespace Koan.Admin.Options;

public sealed class KoanAdminGenerateOptions
{
    private static readonly string[] DefaultComposeProfiles = new[] { "Local" };

    [Required]
    public string[] ComposeProfiles { get; set; } = DefaultComposeProfiles;

    public string[] OpenApiClients { get; set; } = Array.Empty<string>();

    public bool IncludeAppSettings { get; set; } = true;

    public bool IncludeCompose { get; set; } = true;

    public bool IncludeAspire { get; set; } = true;

    public bool IncludeManifest { get; set; } = true;

    public bool IncludeReadme { get; set; } = true;

    public int ComposeBasePort { get; set; } = 5400;
}
