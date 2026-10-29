namespace Koan.Security.Trust.Infrastructure;

internal static class Constants
{
    internal static class Issuer
    {
        /// <summary>The issuer name of a token minted where neither the application nor an owner above Trust names one.</summary>
        public const string Fallback = "koan-dev";
    }

    internal static class DevIdentity
    {
        public const string SubjectQuery = "_as";
        public const string RolesQuery = "_roles";
        public const string Anonymous = "anonymous";
        public const string SubjectClaim = "sub";
        public const string AuthenticationType = "Koan.dev";
    }
}
