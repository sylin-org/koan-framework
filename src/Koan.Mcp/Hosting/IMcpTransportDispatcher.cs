using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Mcp.Hosting;

public interface IMcpTransportDispatcher
{
    Task RunAsync(object target, Stream input, Stream output, CancellationToken cancellationToken);
}
