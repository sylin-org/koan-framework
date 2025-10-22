using Koan.Testing;
using S12.MedTrials.McpService;

namespace Koan.Samples.McpService.Tests;

public class TestPipelineFixture : KoanTestPipelineFixtureBase
{
    public TestPipelineFixture() : base(typeof(Program))
    {
        // Optionally configure test pipeline here
    }
}
