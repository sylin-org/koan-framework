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
        var formatter = ActivatorUtilities.CreateInstance<Koan.Web.Transformers.EntityInputTransformFormatter>(_sp);
        // Put first so it can claim matching content types before JSON
        options.InputFormatters.Insert(0, formatter);
    }
}