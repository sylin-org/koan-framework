namespace Koan.Communication.Connector.RabbitMq;

/// <summary>RabbitMQ Transport connection, flow-control, and mesh-integrity options.</summary>
public sealed class RabbitMqCommunicationOptions
{
    /// <summary>Credential used for automatically discovered endpoints that do not include user information.</summary>
    public string Username { get; set; } = "koan";

    /// <summary>Credential used for automatically discovered endpoints that do not include user information.</summary>
    public string Password { get; set; } = "koan";

    /// <summary>Optional explicit HMAC material. When absent, the authenticated broker credential is used.</summary>
    public string? MeshTrustKey { get; set; }

    /// <summary>Maximum unacknowledged deliveries on the consumer channel.</summary>
    public ushort Prefetch { get; set; } = 32;

    /// <summary>Maximum time allowed for one confirmed publication.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
