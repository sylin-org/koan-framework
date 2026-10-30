namespace Koan.Jobs.Infrastructure;

internal static class Constants
{
    internal static class Operations
    {
        public const string Submit = "job submission";
        public const string Execute = "job execution";
    }

    internal static class Submission
    {
        // Bounds wake-signal churn for long or infinite sources without delaying work until source completion.
        public const int WakeInterval = 64;

        // Jobs one cancellation read loads for a work item (JOBS-0010): it pages until a page falls short.
        public const int CancelPageSize = 100;

        // Times a cancellation reads a job again after its write lost to a change made since its read.
        public const int CancelRereads = 3;
    }

    internal static class Worker
    {
        // The floor for a worker duty whose configured interval is not positive, so no duty can spin.
        public static readonly TimeSpan MinimumPeriod = TimeSpan.FromMilliseconds(10);

        // The longest single timer wait a duty makes: timers refuse more than about 49.7 days, so longer intervals are
        // waited in parts of one day.
        public static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);
    }

    internal static class Retention
    {
        // Rows one retention read loads and removes at a time (JOBS-0010): the sweep pages until a page falls short.
        public const int PageSize = 500;
    }

    internal static class Work
    {
        public const string SingletonId = "__koan_job_singleton__";
    }

    internal static class Diagnostics
    {
        internal static class Codes
        {
            public const string LedgerSelected = "koan.jobs.ledger.selected";
            public const string WakeSelected = "koan.jobs.wake.selected";
            public const string ContextGuarantees = "koan.jobs.context.guarantees";
            public const string DispatchSelected = "koan.jobs.dispatch.selected";
        }

        internal static class Subjects
        {
            public const string Ledger = "jobs:ledger";
            public const string Wake = "jobs:wake";
            public const string Context = "jobs:context";
            public const string Dispatch = "jobs:dispatch";
        }

        internal static class Selections
        {
            public const string InMemory = "in-memory";
            public const string DurableData = "durable-data";
            public const string Pull = "pull-cas";
            public const string Reservation = "reservation-roster";
        }

        internal static class Reasons
        {
            public const string NoDurableAdapter = "no-durable-data-adapter";
            public const string DurableAdapter = "durable-data-adapter";
            public const string CommunicationSignal = "ledger-backed-latency-hint";
            public const string DurableContext = "durable-context-carriage";
            public const string DefaultPull = "default-dispatch-mode";
            public const string ReservationOptIn = "reservation-opt-in";
        }

        internal static class Capabilities
        {
            public const string LogicalContext = "context.logical.host-trusted";
            public const string SharedLedger = "context.ledger.shared-control-plane";
            public const string WorkItemSegmentation = "context.work-item.segmentation-enforced";
            public const string AtLeastOnce = "delivery.at-least-once";
            public const string ContextFreeWake = "wake.context-free";
        }
    }

    internal static class Wake
    {
        public const string ContractId = "koan.jobs.work-ready@1";
        public const string GroupId = "koan.jobs.workers@1";

        // Rows the durable wake stamp spreads its bumps across (JOBS-0010): two submitting transactions meet on one row
        // one time in this many, and a probe reads all of them in one bounded read.
        public const int StampShards = 16;
    }
}
