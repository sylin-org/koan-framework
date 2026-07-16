namespace Koan.Jobs.Infrastructure;

internal static class Constants
{
    internal static class Diagnostics
    {
        internal static class Codes
        {
            public const string LedgerSelected = "koan.jobs.ledger.selected";
            public const string WakeSelected = "koan.jobs.wake.selected";
        }

        internal static class Subjects
        {
            public const string Ledger = "jobs:ledger";
            public const string Wake = "jobs:wake";
        }

        internal static class Selections
        {
            public const string InMemory = "in-memory";
            public const string DurableData = "durable-data";
        }

        internal static class Reasons
        {
            public const string NoDurableAdapter = "no-durable-data-adapter";
            public const string DurableAdapter = "durable-data-adapter";
            public const string CommunicationSignal = "ledger-backed-latency-hint";
        }
    }

    internal static class Wake
    {
        public const string ContractId = "koan.jobs.work-ready@1";
        public const string GroupId = "koan.jobs.workers@1";
    }
}
