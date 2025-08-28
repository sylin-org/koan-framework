namespace Sora.Secrets.Vault;

public sealed class VaultOptions
{
    public bool Enabled { get; set; } = false;
    public Uri? Address { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public string Mount { get; set; } = "secret"; // KV engine mount
    public bool UseKvV2 { get; set; } = true;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);
}
