namespace OrderIntake.Infrastructure;

internal static class OrderIntakeConstants
{
    internal static class Routes
    {
        public const string Trials = "api/trials";
    }

    internal static class Limits
    {
        public const int MinimumOrders = 1;
        public const int DefaultOrders = 100;
        public const int MaximumOrders = 1_000;
        public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    }

    internal static class Corrections
    {
        public const string Documents =
            "Start MongoDB with: docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d mongo";
        public const string Relational =
            "Start PostgreSQL with: docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d postgres";
        public const string KeyValue =
            "Start Redis with: docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d redis";
        public const string Local =
            "Ensure the sample directory is writable and inspect /.well-known/Koan/facts for the Local source.";
    }
}
