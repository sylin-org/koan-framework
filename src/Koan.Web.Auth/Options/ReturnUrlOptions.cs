namespace Koan.Web.Auth.Options;

public sealed class ReturnUrlOptions
{
    public string DefaultPath { get; init; } = "/";
    public string[] AllowList { get; init; } = Array.Empty<string>();
}