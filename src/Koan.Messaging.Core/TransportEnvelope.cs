using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Messaging
{
    /// <summary>
    /// Generic transport envelope for strongly-typed Flow messaging.
    /// </summary>
    public class TransportEnvelope<T>
    {
        public string Version { get; set; } = "1";
        public string? Source { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public T Payload { get; set; } = default!;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

}
