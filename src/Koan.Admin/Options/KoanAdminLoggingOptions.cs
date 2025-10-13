namespace Koan.Admin.Options;

public sealed class KoanAdminLoggingOptions
{
    public bool EnableLogStream { get; set; } = Koan.Core.KoanEnv.IsDevelopment;
    public bool AllowTranscriptDownload { get; set; }
        = Koan.Core.KoanEnv.IsDevelopment;
    public string[] AllowedCategories { get; set; } = Array.Empty<string>();
}
