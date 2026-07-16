namespace Koan.Communication.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        public const string Section = "Koan:Communication";
        public const string InProcessCapacity = Section + ":InProcessCapacity";
        public const string MaxPayloadBytes = Section + ":MaxPayloadBytes";
        public const string TransportProvider = Section + ":TransportProvider";
        public const string EventsProvider = Section + ":EventsProvider";
        public const string FrameworkSignalsProvider = Section + ":FrameworkSignalsProvider";
        public const string FrameworkBroadcastsProvider = Section + ":FrameworkBroadcastsProvider";
        public const string Channels = Section + ":Channels";
    }

    internal static class Channels
    {
        public const string Default = "default";
        public const int MaximumNameLength = 64;
    }

    internal static class Transport
    {
        public const string DefaultChannel = Channels.Default;
        public const string InProcessAdapter = "in-process";
        public const string ProcessMemoryAssurance = "process-memory";
        public const string BuiltInProviderId = "in-process";
    }

    internal static class Events
    {
        public const string DefaultChannel = Channels.Default;
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
        public const string FrameworkSignalsAdapter = "communication.framework-signals.adapter";
        public const string FrameworkBroadcastsAdapter = "communication.framework-broadcasts.adapter";
        public const string Capacity = "communication.in-process-capacity";
        public const string MaxPayloadBytes = "communication.max-payload-bytes";
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
            public const string FrameworkSignalsSelected = "koan.communication.framework-signals.selected";
            public const string FrameworkSignalGroupsDiscovered = "koan.communication.framework-signals.groups.discovered";
            public const string FrameworkBroadcastsSelected = "koan.communication.framework-broadcasts.selected";
            public const string FrameworkBroadcastNodesDiscovered = "koan.communication.framework-broadcasts.nodes.discovered";
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
            public const string FrameworkSignals = "communication:framework-signals:default";
            public const string FrameworkSignalGroups = "communication:framework-signals:groups";
            public const string FrameworkBroadcasts = "communication:framework-broadcasts:default";
            public const string FrameworkBroadcastNodes = "communication:framework-broadcasts:nodes";

            public static string TransportFor(string channel)
                => channel == Channels.Default ? Transport : $"communication:transport:{channel}";

            public static string EventsFor(string channel)
                => channel == Channels.Default ? Events : $"communication:events:{channel}";

            public static string TransportBoundsFor(string channel)
                => channel == Channels.Default ? TransportBounds : $"communication:transport:{channel}:bounds";

            public static string EventsBoundsFor(string channel)
                => channel == Channels.Default ? EventsBounds : $"communication:events:{channel}:bounds";

            public static string ReceiverFor(string channel, string group)
                => channel == Channels.Default ? ReceiverPrefix + group : $"{ReceiverPrefix}{channel}:{group}";

            public static string SubscriptionFor(string channel, string group)
                => channel == Channels.Default ? SubscriptionPrefix + group : $"{SubscriptionPrefix}{channel}:{group}";
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
            public const string ConfirmedPublication = "transport.confirmed-publication";
            public const string RemoteSettlementUnobservable = "transport.remote-settlement-unobservable";
            public const string InternalFrameworkSignals = "framework-signals.internal";
            public const string BestEffortFallback = "framework-signals.best-effort-fallback";
            public const string BoundedSignalEgress = "framework-signals.bounded-egress";
            public const string InternalFrameworkBroadcasts = "framework-broadcasts.internal";
            public const string EveryActiveNode = "framework-broadcasts.every-active-node";
        }
    }
}
