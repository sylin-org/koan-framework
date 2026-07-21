namespace Koan.Communication;

/// <summary>Optional provider bindings for one startup-declared business Communication channel.</summary>
public sealed class CommunicationChannelOptions
{
    /// <summary>Optional Transport provider pin; null uses normal direct-reference or built-in election.</summary>
    public string? TransportProvider { get; set; }

    /// <summary>Optional Events provider pin; null uses normal direct-reference or built-in election.</summary>
    public string? EventsProvider { get; set; }
}
