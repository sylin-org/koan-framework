using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Sources;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Client;

/// <summary>
/// NK-033, AI-0041: every protocol verb (Imagine, Transcribe, Speak, Rerank, Edit, Render) resolves its adapter
/// through the AI source registry. An explicit or scoped source is required, never weakened to another provider;
/// without one the elected provider serves, and two providers with no election is a corrective error.
/// </summary>
[Trait("ADR", "AI-0041")]
[Trait("Category", "Unit")]
public sealed class ProtocolVerbSourceResolution : IDisposable
{
    private readonly IServiceProvider? _initialHost = AppHost.Current;
    private readonly InMemoryAdapterRegistry _adapters = new();
    private readonly AiSourceRegistry _sources = new();

    public ProtocolVerbSourceResolution()
    {
        AppHost.Current = new ServiceCollection()
            .AddSingleton<IAiAdapterRegistry>(_adapters)
            .AddSingleton<IAiSourceRegistry>(_sources)
            .BuildServiceProvider();
    }

    public void Dispose() => AppHost.Current = _initialHost;

    public static IEnumerable<object[]> Verbs() =>
    [
        ["Imagine"], ["Transcribe"], ["Speak"], ["Rerank"], ["Edit"], ["Render"]
    ];

    // Each verb returns the id of the adapter that served it.
    private static async Task<string> Serve(string verb, string? source = null)
    {
        return verb switch
        {
            "Imagine" => Text(source is null
                ? await Koan.AI.Client.Imagine("p")
                : await Koan.AI.Client.Imagine("p", new ImagineOptions { Source = source })),
            "Transcribe" => source is null
                ? await Koan.AI.Client.Transcribe([1])
                : await Koan.AI.Client.Transcribe([1], new TranscribeOptions { Source = source }),
            "Speak" => Text(source is null
                ? await Koan.AI.Client.Speak("t")
                : await Koan.AI.Client.Speak("t", new SpeakOptions { Source = source })),
            "Rerank" => (source is null
                ? await Koan.AI.Client.Rerank("q", ["d"])
                : await Koan.AI.Client.Rerank("q", ["d"], new RerankOptions { Source = source }))[0].Document,
            "Edit" => Text(source is null
                ? await Koan.AI.Client.Edit([1], "i")
                : await Koan.AI.Client.Edit([1], "i", new EditOptions { Source = source })),
            "Render" => Text(source is null
                ? await Koan.AI.Client.Render("p")
                : await Koan.AI.Client.Render("p", new RenderOptions { Source = source })),
            _ => throw new ArgumentOutOfRangeException(nameof(verb))
        };
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static IDisposable ScopeFor(string verb, string source) => verb switch
    {
        "Imagine" => Koan.AI.Client.Scope(imagine: source),
        "Transcribe" => Koan.AI.Client.Scope(transcribe: source),
        "Speak" => Koan.AI.Client.Scope(speak: source),
        "Rerank" => Koan.AI.Client.Scope(rerank: source),
        "Edit" => Koan.AI.Client.Scope(edit: source),
        "Render" => Koan.AI.Client.Scope(render: source),
        _ => throw new ArgumentOutOfRangeException(nameof(verb))
    };

    private void TwoProviders(int fastPriority = 50, int slowPriority = 50)
    {
        _adapters.Add(new ProtocolAdapter("a-provider"));
        _adapters.Add(new ProtocolAdapter("b-provider"));
        _sources.RegisterSource(Source("slow", "a-provider", slowPriority));
        _sources.RegisterSource(Source("fast", "b-provider", fastPriority));
    }

    private static AiSourceDefinition Source(string name, string provider, int priority = 50, bool enabled = true)
        => new()
        {
            Name = name,
            Provider = provider,
            Priority = priority,
            IsEnabled = enabled,
            Members = [new AiMemberDefinition { Name = $"{name}::member-1", ConnectionString = "http://localhost", Order = 0 }]
        };

    [Theory, MemberData(nameof(Verbs))]
    public async Task Explicit_source_selects_its_provider(string verb)
    {
        TwoProviders();

        (await Serve(verb, "fast")).Should().Be("b-provider");
        (await Serve(verb, "slow")).Should().Be("a-provider");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task Scoped_source_selects_its_provider_and_an_explicit_source_overrides_it(string verb)
    {
        TwoProviders();

        using (ScopeFor(verb, "fast"))
        {
            (await Serve(verb)).Should().Be("b-provider");
            (await Serve(verb, "slow")).Should().Be("a-provider");
        }
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task A_member_reference_resolves_through_its_source(string verb)
    {
        TwoProviders();

        (await Serve(verb, "fast::member-1")).Should().Be("b-provider");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task An_unknown_source_is_an_error_naming_the_usable_sources(string verb)
    {
        TwoProviders();

        var error = (await Assert.ThrowsAsync<InvalidOperationException>(() => Serve(verb, "nope")));

        error.Message.Should().Contain("'nope'").And.Contain("fast").And.Contain("slow");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task A_scoped_unknown_source_is_an_error_not_a_fallback(string verb)
    {
        TwoProviders();

        using (ScopeFor(verb, "nope"))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve(verb));
            error.Message.Should().Contain("'nope'");
        }
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task A_disabled_source_is_an_error_not_a_fallback(string verb)
    {
        _adapters.Add(new ProtocolAdapter("a-provider"));
        _adapters.Add(new ProtocolAdapter("b-provider"));
        _sources.RegisterSource(Source("slow", "a-provider"));
        _sources.RegisterSource(Source("fast", "b-provider", enabled: false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve(verb, "fast"));

        error.Message.Should().Contain("'fast'").And.Contain("disabled").And.Contain("slow");
    }

    [Fact]
    public async Task A_source_whose_provider_lacks_the_verb_is_an_error_not_a_fallback()
    {
        _adapters.Add(new ProtocolAdapter("a-provider"));
        _adapters.Add(new ImagineOnlyAdapter("img"));
        _sources.RegisterSource(Source("slow", "a-provider"));
        _sources.RegisterSource(Source("pictures", "img"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve("Transcribe", "pictures"));

        error.Message.Should().Contain("'pictures'").And.Contain("Transcribe").And.Contain("slow");
    }

    [Fact]
    public async Task A_source_whose_provider_is_not_registered_is_an_error()
    {
        _adapters.Add(new ProtocolAdapter("a-provider"));
        _sources.RegisterSource(Source("ghost", "missing-provider"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve("Imagine", "ghost"));

        error.Message.Should().Contain("missing-provider").And.Contain("a-provider");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task One_provider_serves_when_no_source_is_named(string verb)
    {
        _adapters.Add(new ProtocolAdapter("only"));

        (await Serve(verb)).Should().Be("only");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task Two_providers_without_an_election_are_a_corrective_error(string verb)
    {
        TwoProviders();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve(verb));

        error.Message.Should().Contain("a-provider").And.Contain("b-provider").And.Contain("Source");
    }

    [Theory, MemberData(nameof(Verbs))]
    public async Task The_highest_priority_source_elects_its_provider(string verb)
    {
        TwoProviders(fastPriority: 80, slowPriority: 50);

        (await Serve(verb)).Should().Be("b-provider");
    }

    [Fact]
    public async Task A_provider_whose_sources_are_all_disabled_is_not_elected()
    {
        _adapters.Add(new ProtocolAdapter("a-provider"));
        _adapters.Add(new ProtocolAdapter("b-provider"));
        _sources.RegisterSource(Source("slow", "a-provider"));
        _sources.RegisterSource(Source("fast", "b-provider", enabled: false));

        (await Serve("Imagine")).Should().Be("a-provider");
    }

    [Fact]
    public async Task No_provider_at_all_is_a_corrective_error()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Serve("Imagine"));

        error.Message.Should().Contain("IImagineAdapter").And.Contain("Imagine");
    }

    private sealed class ProtocolAdapter(string id) :
        IImagineAdapter, ITranscribeAdapter, ISpeakAdapter, IRerankAdapter, IEditAdapter, IRenderAdapter
    {
        public string Id => id;
        public string Name => id;
        public string Type => id;

        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);

        private byte[] Mark => Encoding.UTF8.GetBytes(id);

        public Task<ImagineResponse> Imagine(ImagineRequest request, CancellationToken ct = default)
            => Task.FromResult(new ImagineResponse { Image = Mark });

        public Task<TranscribeResponse> Transcribe(TranscribeRequest request, CancellationToken ct = default)
            => Task.FromResult(new TranscribeResponse { Text = id });

        public IAsyncEnumerable<TranscribeSegment>? StreamTranscribe(
            Stream audioStream, TranscribeRequest request, CancellationToken ct = default) => null;

        public Task<SpeakResponse> Speak(SpeakRequest request, CancellationToken ct = default)
            => Task.FromResult(new SpeakResponse { Audio = Mark });

        public IAsyncEnumerable<ReadOnlyMemory<byte>>? StreamSpeak(
            SpeakRequest request, CancellationToken ct = default) => null;

        public Task<RerankResponse> Rerank(RerankRequest request, CancellationToken ct = default)
            => Task.FromResult(new RerankResponse
            {
                Documents = [new RankedDocument { Index = 0, Document = id, Score = 1 }]
            });

        public Task<EditResponse> Edit(EditRequest request, CancellationToken ct = default)
            => Task.FromResult(new EditResponse { Image = Mark });

        public Task<RenderResponse> Render(RenderRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderResponse { Video = Mark });
    }

    private sealed class ImagineOnlyAdapter(string id) : IImagineAdapter
    {
        public string Id => id;
        public string Name => id;
        public string Type => id;

        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);

        public Task<ImagineResponse> Imagine(ImagineRequest request, CancellationToken ct = default)
            => Task.FromResult(new ImagineResponse { Image = Encoding.UTF8.GetBytes(id) });
    }
}
