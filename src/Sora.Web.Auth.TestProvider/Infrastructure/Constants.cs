namespace Sora.Web.Auth.TestProvider.Infrastructure;

internal static class Constants
{
    // Dev persona cookie used by the TestProvider login UI
    public const string CookieUser = "_tp_user";

    // Prefix for custom claim entries passed via query string
    public const string ClaimPrefix = "claim.";
}
