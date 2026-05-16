using System;
using Koan.Jobs.Archival;

namespace Koan.Jobs.Core.Tests.Specs.Archival;

public class JobArchivalPolicySpec
{
    [Fact(DisplayName = "ArchivalPolicy: default has 30-day retention")]
    public void Default_policy_has_30_day_retention()
    {
        var policy = new JobArchivalPolicy();

        policy.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact(DisplayName = "ArchivalPolicy: default has 500 batch size")]
    public void Default_policy_has_500_batch_size()
    {
        var policy = new JobArchivalPolicy();

        policy.BatchSize.Should().Be(500);
    }

    [Fact(DisplayName = "ArchivalPolicy: default is enabled")]
    public void Default_policy_is_enabled()
    {
        var policy = new JobArchivalPolicy();

        policy.Enabled.Should().BeTrue();
    }

    [Fact(DisplayName = "ArchivalPolicy: supports custom values")]
    public void Policy_supports_custom_values()
    {
        var policy = new JobArchivalPolicy
        {
            RetentionPeriod = TimeSpan.FromDays(90),
            BatchSize = 1000,
            SweepInterval = TimeSpan.FromHours(12),
            Enabled = false
        };

        policy.RetentionPeriod.Should().Be(TimeSpan.FromDays(90));
        policy.BatchSize.Should().Be(1000);
        policy.SweepInterval.Should().Be(TimeSpan.FromHours(12));
        policy.Enabled.Should().BeFalse();
    }
}
