using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// WEB-0074: the one controller on the matrix that declares <c>?q=</c> a provider-native query. Its sibling
/// <see cref="WidgetController"/> does not, which is what the refusal specs rely on.
/// </summary>
[Route("api/raw-widgets")]
public sealed class RawWidgetController : EntityController<Widget>
{
    protected override bool AllowRawQuery => true;
}
