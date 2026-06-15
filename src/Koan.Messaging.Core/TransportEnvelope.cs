using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Messaging
{
    /// <summary>
    /// Generic transport envelope for strongly-typed messaging payloads.
    /// </summary>
    public class TransportEnvelope<T>
    {
        public string Version { get; set; } = "1";
        public string? Source { get; set; }
        public string Model { get; set; } = "";
        public string Type { get; set; } = "";
        public T Payload { get; set; } = default!;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, object?> Metadata { get; set; } = new();
    }

}
