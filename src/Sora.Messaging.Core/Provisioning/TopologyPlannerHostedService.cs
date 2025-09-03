using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Messaging.Provisioning;

namespace Sora.Messaging.Core.Provisioning
{
    /// <summary>
    /// Hosted service that runs the topology planner at startup.
    /// </summary>
    public sealed class TopologyPlannerHostedService : IHostedService
    {
        private readonly ITopologyPlanner _planner;
        private readonly ILogger<TopologyPlannerHostedService> _logger;

        public TopologyPlannerHostedService(ITopologyPlanner planner, ILogger<TopologyPlannerHostedService> logger)
        {
            _planner = planner;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Sora.Messaging: Running topology planner at startup...");
            if (_planner is DefaultTopologyPlanner defaultPlanner)
            {
                await defaultPlanner.PlanAndProvisionAsync(cancellationToken);
                _logger.LogInformation("Sora.Messaging: Topology planner completed.");
            }
            else
            {
                _logger.LogWarning("Sora.Messaging: Topology planner is not DefaultTopologyPlanner. Skipping auto-provisioning.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
