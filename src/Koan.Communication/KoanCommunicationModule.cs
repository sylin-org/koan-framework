using Koan.Core;
using Koan.Core.Semantics;
using Koan.Core.Composition;
using Koan.Communication.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Communication;

/// <summary>Reference = Intent boot module for Entity Communication.</summary>
public sealed class KoanCommunicationModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddKoanCommunication();

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        => CommunicationCompositionFacts.Project(composition, services, GetType().FullName ?? Id);
}
