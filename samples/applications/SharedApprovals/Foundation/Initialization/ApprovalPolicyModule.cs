using Example.Approvals.Foundation.Domain;
using Example.Approvals.Foundation.Policy;
using Example.Approvals.Foundation.Web;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Example.Approvals.Foundation.Initialization;

/// <summary>Binds the organization's approval policy to a consumer's business Entity.</summary>
public abstract class ApprovalPolicyModule<TEntity> : KoanModule
    where TEntity : ApprovalRequest<TEntity>, new()
{
    private readonly ApprovalPolicyOptions _options = new();

    public override void Register(IServiceCollection services)
    {
        var policy = new ApprovalPolicy(_options);
        ApprovalRequest<TEntity>.Lifecycle.BeforeUpsert(policy.BeforeUpsert<TEntity>).BeforeRemove(policy.BeforeRemove<TEntity>);
        services.AddSingleton(_options);
        services.Configure<MvcOptions>(options => options.Filters.Add<ApprovalExceptionFilter>());
        services.AddKoanControllersFrom<ApprovalPolicyController>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version, $"Shared approval policy for {typeof(TEntity).Name}.");
        module.AddNote($"Approval limit: {_options.Currency} {_options.MaximumApprovalAmount:0.00}; approved common fields are final.");
    }
}
