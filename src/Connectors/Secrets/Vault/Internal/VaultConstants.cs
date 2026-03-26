namespace Koan.Secrets.Connector.Vault.Internal;

internal static class VaultConstants
{
    public const string ConfigPath = "Koan:Secrets:Vault";
    public const string HttpClientName = "Koan.Secrets.Connector.Vault";

    public static class Keys
    {
        public const string Address = ConfigPath + ":Address";
        public const string DisableAutoDetection = ConfigPath + ":DisableAutoDetection";
    }
}

