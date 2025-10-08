using Koan.Testing;
using S16.PantryPal;

namespace Koan.Samples.PantryPal.Tests;

public class TestPipelineFixture : KoanTestPipelineFixtureBase
{
    public TestPipelineFixture() : base(typeof(Program))
    {
        // Optionally configure test pipeline here
    }
}
