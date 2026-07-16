using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Policies;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheBehaviorSpec
{
    [Fact]
    public void CacheBehavior_DeclaresFourModes()
    {
        var values = Enum.GetValues<CacheBehavior>();

        values.Should().BeEquivalentTo(new[]
        {
            CacheBehavior.Default,
            CacheBehavior.Bypass,
            CacheBehavior.Refresh,
            CacheBehavior.ReadOnly
        });
    }

    [Fact]
    public void CoherenceMode_DeclaresThreeStates()
    {
        var values = Enum.GetValues<CoherenceMode>();

        values.Should().BeEquivalentTo(new[]
        {
            CoherenceMode.AutoDetect,
            CoherenceMode.Required,
            CoherenceMode.Disabled
        });
    }
}
