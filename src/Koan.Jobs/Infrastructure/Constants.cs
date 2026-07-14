namespace Koan.Jobs.Infrastructure;

internal static class Constants
{
    internal static class Diagnostics
    {
        internal static class Codes
        {
            public const string LedgerSelected = "koan.jobs.ledger.selected";
            public const string TransportSelected = "koan.jobs.transport.selected";
        }

        internal static class Subjects
        {
            public const string Ledger = "jobs:ledger";
            public const string Transport = "jobs:transport";
        }

        internal static class Selections
        {
            public const string InMemory = "in-memory";
            public const string DurableData = "durable-data";
            public const string InProcess = "in-process";
            public const string Custom = "custom";
        }

        internal static class Reasons
        {
            public const string NoDurableAdapter = "no-durable-data-adapter";
            public const string DurableAdapter = "durable-data-adapter";
            public const string DefaultTransport = "default-transport";
            public const string RegisteredTransport = "registered-transport";
        }
    }
}
