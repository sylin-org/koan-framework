using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Extensions;

internal sealed class OptionalTransformerInputFormatterConfigurator : IConfigureOptions<MvcOptions>
{
    private readonly IServiceProvider _sp;
    public OptionalTransformerInputFormatterConfigurator(IServiceProvider sp) => _sp = sp;

    public void Configure(MvcOptions options)
    {
        try
        {
            var formatterType = Type.GetType("Koan.Web.Transformers.EntityInputTransformFormatter, Koan.Web.Transformers");
            if (formatterType is null) return;
            var formatter = (Microsoft.AspNetCore.Mvc.Formatters.IInputFormatter?)ActivatorUtilities.CreateInstance(_sp, formatterType);
            if (formatter is not null)
            {
                // Put first so it can claim matching content types before JSON
                options.InputFormatters.Insert(0, formatter);
            }
        }
        catch { /* optional */ }
    }
}