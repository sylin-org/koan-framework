namespace Sora.Secrets.Core.DI;

public sealed class SecretsOptions
{
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);
}