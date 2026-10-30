namespace Koan.Web.Auth.Connector.Atproto.Options;

public sealed class AtprotoOptions
{
    public string PlcDirectory { get; set; } = "https://plc.directory";
    /// <summary>Alternate PLC only for identities explicitly listed in DevelopmentHandles.</summary>
    public string? DevelopmentPlcDirectory { get; set; }
    /// <summary>Socket destination for exact allowed development origins; URLs and DPoP audiences remain unchanged.</summary>
    public string? DevelopmentConnectHost { get; set; }
    /// <summary>
    /// The largest protocol document the connector reads itself: identity and authorization-server discovery, the pushed
    /// request, token and revocation answers. A response sent for a caller takes the caller's limit instead.
    /// </summary>
    public int MaximumProtocolResponseBytes { get; set; } = 4 * 1024 * 1024;
    public int RequestTimeoutSeconds { get; set; } = 30;
    /// <summary>
    /// How many signed-in clients one process keeps ready, one per DID; past it, the one used least recently is closed and
    /// rebuilt from its session row when next used. It sizes memory and nothing a person meets.
    /// </summary>
    public int CachedClients { get; set; } = 1024;
    /// <summary>Exact local origins admitted only in Development. There is no production override.</summary>
    public string[] DevelopmentAllowedOrigins { get; set; } = [];
    /// <summary>Explicit local DNS fixtures; real DID documents must still reverse-bind the supplied handle.</summary>
    public Dictionary<string, string> DevelopmentHandles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal string DirectoryFor(string identifier, bool development)
        => development && DevelopmentPlcDirectory is not null
           && (DevelopmentHandles.ContainsKey(identifier) || DevelopmentHandles.Values.Contains(identifier, StringComparer.Ordinal))
            ? DevelopmentPlcDirectory : PlcDirectory;
}
