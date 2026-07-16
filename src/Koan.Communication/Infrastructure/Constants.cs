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

    internal static class Events
    {
        public const string DefaultChannel = "default";
        public const string InProcessAdapter = "in-process";
        public const string ProcessMemoryAssurance = "process-memory";
    }

    internal static class Operations
    {
        public const string Raise = "Entity Event raise";
        public const string Send = "Entity Transport send";
    }

    internal static class Provenance
    {
        public const string Adapter = "communication.transport.adapter";
        public const string Assurance = "communication.transport.assurance";
        public const string ReceiverGroups = "communication.transport.receiver-groups";
        public const string EventAdapter = "communication.events.adapter";
        public const string EventAssurance = "communication.events.assurance";
        public const string EventSubscriptions = "communication.events.subscription-groups";
        public const string Capacity = "communication.transport.in-process-capacity";
        public const string MaxPayloadBytes = "communication.transport.max-payload-bytes";
    }

    internal static class Diagnostics
    {
        internal static class Codes
        {
            public const string TransportSelected = "koan.communication.transport.selected";
            public const string EventsSelected = "koan.communication.events.selected";
            public const string ReceiversDiscovered = "koan.communication.transport.receivers.discovered";
            public const string ReceiverDiscovered = "koan.communication.transport.receiver.discovered";
            public const string SubscriptionsDiscovered = "koan.communication.events.subscriptions.discovered";
            public const string SubscriptionDiscovered = "koan.communication.events.subscription.discovered";
            public const string TransportBounds = "koan.communication.transport.bounds";
            public const string EventsBounds = "koan.communication.events.bounds";
            public const string ContextCarriage = "koan.communication.context.carriage";
        }

        internal static class Subjects
        {
            public const string Transport = "communication:transport:default";
            public const string Events = "communication:events:default";
            public const string Receivers = "communication:transport:receivers";
            public const string ReceiverPrefix = "communication:transport:receiver:";
            public const string Subscriptions = "communication:events:subscriptions";
            public const string SubscriptionPrefix = "communication:events:subscription:";
            public const string TransportBounds = "communication:transport:bounds";
            public const string EventsBounds = "communication:events:bounds";
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
            public const string EventsScalar = "events.scalar";
            public const string EventsSet = "events.set";
            public const string EventsStream = "events.stream";
            public const string OccurrenceIdentity = "events.occurrence-identity";
            public const string TypedSubscriptions = "events.typed-subscriptions";
            public const string ZeroSubscriberAcceptance = "events.zero-subscriber-acceptance";
            public const string EventContextCarriage = "events.context-carriage";
            public const string EventLocalSettlement = "events.local-settlement";
            public const string EventBoundedIngress = "events.bounded-ingress";
        }
    }
}
