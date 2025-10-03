using Microsoft.AspNetCore.SignalR;

namespace S14.AdapterBench.Hubs;

/// <summary>
/// SignalR hub for real-time benchmark progress updates.
/// Clients subscribe to receive progress notifications during benchmark execution.
/// </summary>
public class BenchmarkHub : Hub
{
    public async Task Subscribe()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "BenchmarkProgress");
    }

    public async Task Unsubscribe()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "BenchmarkProgress");
    }
}
