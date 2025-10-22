using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Xunit.Abstractions;

namespace Koan.Tests.AI.Unit.Specs.Conversation;

public sealed class AiConversationBuilderSpec
{
    private readonly ITestOutputHelper _output;

    public AiConversationBuilderSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Build_populates_context_and_augmentations()
        => TestPipeline.For<AiConversationBuilderSpec>(_output, nameof(Build_populates_context_and_augmentations))
            .Assert(_ =>
            {
                var fake = new FakeAi();
                var builder = new AiConversationBuilder(fake)
                    .WithSystem("system prompt")
                    .WithUser("hello world")
                    .WithProfile("support")
                    .WithBudget("standard")
                    .WithContextTag("tenant", "acme")
                    .WithGroundingReference("doc-42")
                    .WithModel("glm-1")
                    .WithRouteAdapter("ollama:test")
                    .WithRoutePolicy("wrw")
                    .WithAugmentation("rag", configure: p => p["dataset"] = "kb")
                    .WithAugmentation("moderation", enabled: false)
                    .ConfigureOptions(o => o with { Temperature = 0.2, Profile = "support" });

                var request = builder.Build();

                request.Messages.Select(m => m.Role).Should().Contain(new[] { "system", "user" });
                request.Model.Should().Be("glm-1");
                request.Route.Should().NotBeNull();
                request.Route!.AdapterId.Should().Be("ollama:test");
                request.Route.Policy.Should().Be("wrw");
                request.Context.Should().NotBeNull();
                request.Context!.Profile.Should().Be("support");
                request.Context.Budget.Should().Be("standard");
                request.Context.Tags.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
                request.Context.GroundingReferences.Should().Contain("doc-42");
                request.Augmentations.Should().HaveCount(2);
                request.Augmentations.First().Parameters.Should().ContainKey("dataset");
                request.Options.Should().NotBeNull();
                request.Options!.Temperature.Should().Be(0.2);
                request.Options.Profile.Should().Be("support");
                return ValueTask.CompletedTask;
            })
            .RunAsync();

    [Fact]
    public Task SendAsync_delegates_to_underlying_ai()
        => TestPipeline.For<AiConversationBuilderSpec>(_output, nameof(SendAsync_delegates_to_underlying_ai))
            .Assert(async _ =>
            {
                var fake = new FakeAi();
                var builder = new AiConversationBuilder(fake)
                    .WithUser("ping");

                var response = await builder.SendAsync(CancellationToken.None);

                response.Text.Should().Be("ok");
                fake.LastRequest.Should().NotBeNull();
                fake.LastRequest!.Messages.Should().ContainSingle(m => m.Role == "user" && m.Content == "ping");
            })
            .RunAsync();

    [Fact]
    public Task AskAsync_appends_user_turn_before_sending()
        => TestPipeline.For<AiConversationBuilderSpec>(_output, nameof(AskAsync_appends_user_turn_before_sending))
            .Assert(async _ =>
            {
                var fake = new FakeAi();
                var builder = new AiConversationBuilder(fake)
                    .WithSystem("sys");

                await builder.AskAsync("hello", CancellationToken.None);

                fake.LastRequest.Should().NotBeNull();
                fake.LastRequest!.Messages.Should().Contain(m => m.Role == "system");
                fake.LastRequest!.Messages.Should().Contain(m => m.Role == "user" && m.Content == "hello");
            })
            .RunAsync();

    private sealed class FakeAi : IAi
    {
        public AiChatRequest? LastRequest { get; private set; }

        public Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiChatResponse { Text = "ok" });
        }

        public IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return EmptyStream(ct);
        }

        public Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse());

        public Task<string> PromptAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
            => Task.FromResult("ok");

        public IAsyncEnumerable<AiChatChunk> StreamAsync(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
            => EmptyStream(ct);

        private static async IAsyncEnumerable<AiChatChunk> EmptyStream([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
