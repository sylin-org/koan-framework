using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Messaging
{
    /// <summary>
    /// Unified transport envelope for all Sora messaging.
    /// </summary>
    public class TransportEnvelope
    {
        public string Version { get; set; } = "1";
        public string Source { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public object Payload { get; set; } = default!;
    }

    /// <summary>
    /// Messaging extensions for sending envelopes with transformer support.
    /// </summary>
    public static class MessagingEnvelopeExtensions
    {
        // TODO: Implement transformer registry and FlowAdapter metadata injection
        public static async Task SendEnvelope(this TransportEnvelope envelope, CancellationToken? token = null)
        {
            // TODO: Lookup transformer by envelope.Type and apply if registered
            // For now, just send the envelope as-is
            if (token.HasValue)
                await MessagingExtensions.Send(envelope, cancellationToken: token.Value);
            else
                await MessagingExtensions.Send(envelope);
        }
    }
}
