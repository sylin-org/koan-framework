using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Pipeline;

namespace Koan.Web.Context;

/// <summary>Projects Web-owned request read context into Data's existing axis-neutral read fold.</summary>
internal sealed class WebContextReadFilterContributor : IReadFilterContributor
{
    public Filter? ReadFilter(Type entityType) => WebContext.CurrentReadFilter(entityType);

    public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
}
