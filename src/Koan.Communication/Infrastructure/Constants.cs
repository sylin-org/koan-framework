namespace Koan.Communication.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        public const string Section = "Koan:Communication";
        public const string InProcessCapacity = Section + ":InProcessCapacity";
        public const string MaxPayloadBytes = Section + ":MaxPayloadBytes";
    }

    internal static class Transport
    {
        public const string DefaultChannel = "default";
        public const string InProcessAdapter = "in-process";
        public const string ProcessMemoryAssurance = "process-memory";
    }

    internal static class Operations
    {
        public const string Send = "Entity Transport send";
    }

    internal static class Provenance
    {
        public const string Adapter = "communication.transport.adapter";
        public const string Assurance = "communication.transport.assurance";
        public const string ReceiverGroups = "communication.transport.receiver-groups";
        public const string Capacity = "communication.transport.in-process-capacity";
        public const string MaxPayloadBytes = "communication.transport.max-payload-bytes";
    }

    internal static class Diagnostics
    {
        internal static class Codes
        {
            public const string TransportSelected = "koan.communication.transport.selected";
            public const string ReceiversDiscovered = "koan.communication.transport.receivers.discovered";
            public const string ReceiverDiscovered = "koan.communication.transport.receiver.discovered";
            public const string TransportBounds = "koan.communication.transport.bounds";
            public const string ContextCarriage = "koan.communication.context.carriage";
        }

        internal static class Subjects
        {
            public const string Transport = "communication:transport:default";
            public const string Receivers = "communication:transport:receivers";
            public const string ReceiverPrefix = "communication:transport:receiver:";
            public const string Bounds = "communication:transport:bounds";
            public const string Context = "communication:context";
        }

        internal static class Reasons
        {
            public const string BuiltInFloor = "built-in-floor";
            public const string TypedDiscovery = "typed-discovery";
            public const string BoundedProcessMemory = "bounded-process-memory";
            public const string HostContextCarriers = "host-context-carriers";
        }

        internal static class Capabilities
        {
            public const string Scalar = "transport.scalar";
            public const string Set = "transport.set";
            public const string Stream = "transport.stream";
            public const string SnapshotCopy = "transport.snapshot-copy";
            public const string TypedReceivers = "transport.typed-receivers";
            public const string ContextCarriage = "transport.context-carriage";
            public const string LocalSettlement = "transport.local-settlement";
            public const string BoundedIngress = "transport.bounded-ingress";
        }
    }
}
