using System.Threading;
using System.Threading.Tasks;

public interface IFlowSeeder
{
    Task<object> SeedAllAdaptersAsync(int count, CancellationToken ct);
}
