using Koan.Communication.Connector.RabbitMq.Infrastructure;

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

    /// <summary>Maximum unacknowledged deliveries held by each receiver group's consumer.</summary>
    public ushort Prefetch { get; set; } = 32;

    /// <summary>Maximum time allowed for one confirmed publication.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The broker's <c>max_message_size</c>: Koan rejects a larger publication before it reaches RabbitMQ, which would
    /// otherwise close the publishing channel. The default is RabbitMQ 4's; set it when the broker is configured
    /// differently.
    /// </summary>
    public int MaxMessageBytes { get; set; } = Constants.Broker.DefaultMaxMessageBytes;
}
