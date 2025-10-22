using System;
using Microsoft.Extensions.DependencyInjection;
using S12.MedTrials.Services;

namespace S12.MedTrials;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMedTrialsCore(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddScoped<IProtocolDocumentService, ProtocolDocumentService>();
        services.AddScoped<IVisitPlanningService, VisitPlanningService>();
        services.AddScoped<ISafetyDigestService, SafetyDigestService>();

        return services;
    }
}
