using System;

namespace Koan.Testing.Flow;

/// <summary>
/// Shared test constants for Flow-related tests across adapters.
/// </summary>
public static class FlowTestConstants
{
    public static class Keys
    {
        public const string Email = "email";
        public const string Phone = "phone";
        public const string Handle = "handle";
    }

    /// <summary>
    /// Ubiquitous aggregation keys that all adapters should recognize for tests.
    /// </summary>
    public static readonly string[] UbiquitousAggregationTags = new[] { Keys.Email, Keys.Phone, Keys.Handle };

    public static class Samples
    {
        public const string EmailA = "a@example.com";
        public const string EmailB = "b@example.com";
        public const string HandleA = "@alice";
        public const string HandleB = "@bob";
        public const string PhoneA = "+1-202-555-0101";
        public const string PhoneB = "+1-202-555-0102";
    }
}
