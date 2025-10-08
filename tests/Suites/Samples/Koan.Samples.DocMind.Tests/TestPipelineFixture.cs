using Koan.Testing;
using S13.DocMind;

namespace Koan.Samples.DocMind.Tests;

public class TestPipelineFixture : KoanTestPipelineFixtureBase
{
    public TestPipelineFixture() : base(typeof(Program))
    {
        // Optionally configure test pipeline here
    }
}
