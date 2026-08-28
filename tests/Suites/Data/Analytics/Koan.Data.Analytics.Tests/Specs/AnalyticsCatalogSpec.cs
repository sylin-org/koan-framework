namespace Koan.Data.Analytics.Tests.Specs;

/// <summary>
/// The catalog is the shared vocabulary: declaration is unique, listing is deterministic, and asking for
/// a question that does not exist refuses loudly — listing what IS declared — while recording the gap.
/// </summary>
public sealed class AnalyticsCatalogSpec
{
    [Fact]
    public void Declared_names_are_listed_deterministically()
    {
        Analytics.Question<AnalyticsProbe, string>("catalog-zeta", q => q.Count());
        Analytics.Question<AnalyticsProbe, string>("catalog-alpha", q => q.Count());

        var names = AnalyticsCatalog.Names().Where(n => n.StartsWith("catalog-", StringComparison.Ordinal)).ToArray();

        names.Should().Equal("catalog-alpha", "catalog-zeta");
    }

    [Fact]
    public void A_duplicate_name_is_a_declaration_failure_not_an_overwrite()
    {
        Analytics.Question<AnalyticsProbe, string>("duplicate-outside-catalog-prefix", q => q.Count());

        var duplicate = FluentActions.Invoking(() =>
            Analytics.Question<AnalyticsProbe, string>("duplicate-outside-catalog-prefix", q => q.Count()));

        duplicate.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("already declared");
    }

    [Fact]
    public void An_unknown_name_refuses_with_the_catalog_and_records_the_gap()
    {
        var before = AnalyticsGapLog.TotalCount;

        var ask = FluentActions.Awaiting(() => Analytics.Of<AnalyticsProbe, string>().Run("catalog-spec-no-such-question"));

        var refusal = ask.Should().ThrowAsync<KeyNotFoundException>();
        AnalyticsGapLog.TotalCount.Should().Be(before + 1,
            "an unknown ask is a coverage signal and must be recorded, not just refused");
        AnalyticsGapLog.Recent(5).Select(g => g.Name).Should().Contain("catalog-spec-no-such-question");
    }
}
