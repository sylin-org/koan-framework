using System.Net;
using Koan.Data.Vector.Connector.Qdrant;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Data.Core.Specs.Vector;

public sealed class QdrantHealthContributorCancellationSpec
{
    [Fact]
    public async Task Host_cancellation_is_not_converted_to_an_unhealthy_report()
    {
        var handler = new BlockingHandler();
        var contributor = new QdrantHealthContributor(
            new StubHttpClientFactory(handler),
            Options.Create(new QdrantOptions()),
            NullLogger<QdrantHealthContributor>.Instance);
        using var stopping = new CancellationTokenSource();

        var check = contributor.Check(stopping.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stopping.Cancel();

        var act = async () => await check;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "intentional host shutdown is not a Qdrant health failure");
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
