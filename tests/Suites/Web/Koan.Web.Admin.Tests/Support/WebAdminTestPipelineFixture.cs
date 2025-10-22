using Koan.TestPipeline;

namespace Koan.Web.Admin.Tests.Support;

public class WebAdminTestPipelineFixture : TestPipelineFixture
{
    public WebAdminTestPipelineFixture() : base("web-admin")
    {
        // Additional setup for web admin HTTP pipeline
    }
}
