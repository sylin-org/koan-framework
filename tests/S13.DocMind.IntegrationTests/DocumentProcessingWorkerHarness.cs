using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;

namespace S13.DocMind.IntegrationTests;

internal sealed class DocumentProcessingWorkerHarness
{
    private readonly DocumentProcessingWorker _worker;
    private readonly MethodInfo _processBatchMethod;

    public DocumentProcessingWorkerHarness(IServiceProvider services, TimeProvider clock)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var options = services.GetRequiredService<IOptions<DocMindOptions>>();
        _worker = new DocumentProcessingWorker(scopeFactory, options, clock, NullLogger<DocumentProcessingWorker>.Instance);
        _processBatchMethod = typeof(DocumentProcessingWorker).GetMethod("ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessBatchAsync method could not be located.");
    }

    public Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var task = (Task<bool>)_processBatchMethod.Invoke(_worker, new object[] { cancellationToken })!;
        return task;
    }
}
