namespace Koan.Data.Analytics.Infrastructure;

public static class Constants
{
    public const string Section = "Koan:Data:Analytics";

    /// <summary>Maximum rows a single on-demand answer may carry before it is capped (and labeled).</summary>
    public const int DefaultRowCap = 1_000;
    public const int MaximumRowCap = 100_000;

    /// <summary>Wall-clock ceiling for one on-demand ask. A bounded ask that cannot finish is a refusal, not a hang.</summary>
    public const int DefaultTimeoutSeconds = 5;

    /// <summary>Unknown-question asks retained for the request-a-recipe loop.</summary>
    public const int GapLogCapacity = 256;

    public static class Configuration
    {
        public const string RowCap = Section + ":RowCap";
        public const string TimeoutSeconds = Section + ":TimeoutSeconds";
    }
}
