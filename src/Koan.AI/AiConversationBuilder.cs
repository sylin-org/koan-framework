using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;

namespace Koan.AI;

/// <summary>
/// Fluent builder that composes a conversation-centric <see cref="AiChatRequest"/> and executes it via <see cref="IAi"/>.
/// </summary>
public sealed class AiConversationBuilder
{
    private readonly IAi _ai;
    private readonly List<AiMessage> _messages = new();
    private readonly List<AiAugmentationInvocation> _augmentations = new();
    private readonly Dictionary<string, string> _contextTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _groundingReferences = new();
    private AiPromptOptions? _options;
    private string? _model;
    private string? _profile;
    private string? _budget;
    private AiRouteHints? _route;

    internal AiConversationBuilder(IAi ai)
        => _ai = ai ?? throw new ArgumentNullException(nameof(ai));

    public AiConversationBuilder WithMessage(AiMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        _messages.Add(message);
        return this;
    }

    public AiConversationBuilder WithMessages(IEnumerable<AiMessage> messages)
    {
        if (messages is null) throw new ArgumentNullException(nameof(messages));
        foreach (var message in messages)
        {
            WithMessage(message);
        }
        return this;
    }

    public AiConversationBuilder WithSystem(string text)
        => AddTextMessage("system", text);

    public AiConversationBuilder WithUser(string text)
        => AddTextMessage("user", text);

    public AiConversationBuilder WithAssistant(string text)
        => AddTextMessage("assistant", text);

    public AiConversationBuilder WithTool(string name, string content, string? callId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Tool content is required.", nameof(content));

        var message = new AiMessage("tool", content)
        {
            Name = name,
            ToolCallId = callId
        };
        return WithMessage(message);
    }

    public AiConversationBuilder WithModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("Model name is required.", nameof(model));
        _model = model.Trim();
        return this;
    }

    public AiConversationBuilder WithProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Profile name is required.", nameof(profile));
        _profile = profile.Trim();
        return this;
    }

    public AiConversationBuilder WithBudget(string budget)
    {
        if (string.IsNullOrWhiteSpace(budget)) throw new ArgumentException("Budget identifier is required.", nameof(budget));
        _budget = budget.Trim();
        return this;
    }

    public AiConversationBuilder WithContextTag(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Context key is required.", nameof(key));
        if (value is null) throw new ArgumentNullException(nameof(value));
        _contextTags[key.Trim()] = value;
        return this;
    }

    public AiConversationBuilder WithGroundingReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("Grounding reference is required.", nameof(reference));
        _groundingReferences.Add(reference.Trim());
        return this;
    }

    public AiConversationBuilder ConfigureOptions(Func<AiPromptOptions, AiPromptOptions> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var configured = configure(_options ?? new AiPromptOptions())
            ?? throw new InvalidOperationException("ConfigureOptions must return an AiPromptOptions instance.");
        _options = configured;
        return this;
    }

    public AiConversationBuilder WithOptions(AiPromptOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    public AiConversationBuilder WithRoute(AiRouteHints hints)
    {
        _route = hints ?? throw new ArgumentNullException(nameof(hints));
        return this;
    }

    public AiConversationBuilder WithRouteAdapter(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) throw new ArgumentException("Adapter ID is required.", nameof(adapterId));
        _route = (_route ?? new AiRouteHints()) with { AdapterId = adapterId.Trim() };
        return this;
    }

    public AiConversationBuilder WithRoutePolicy(string policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) throw new ArgumentException("Policy is required.", nameof(policy));
        _route = (_route ?? new AiRouteHints()) with { Policy = policy.Trim() };
        return this;
    }

    public AiConversationBuilder WithRouteStickyKey(string stickyKey)
    {
        if (string.IsNullOrWhiteSpace(stickyKey)) throw new ArgumentException("Sticky key is required.", nameof(stickyKey));
        _route = (_route ?? new AiRouteHints()) with { StickyKey = stickyKey.Trim() };
        return this;
    }

    public AiConversationBuilder WithAugmentation(string name, bool enabled = true, Action<IDictionary<string, object?>>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Augmentation name is required.", nameof(name));

        IDictionary<string, object?>? parameters = null;
        if (configure is not null)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            configure(dict);
            if (dict.Count > 0)
            {
                parameters = dict;
            }
        }

        _augmentations.Add(new AiAugmentationInvocation
        {
            Name = name.Trim(),
            Enabled = enabled,
            Parameters = parameters
        });

        return this;
    }

    public AiChatRequest Build()
    {
        if (_messages.Count == 0)
        {
            throw new InvalidOperationException("Conversation must contain at least one message.");
        }

        AiConversationContext? context = null;
        if (!string.IsNullOrWhiteSpace(_profile) ||
            !string.IsNullOrWhiteSpace(_budget) ||
            _contextTags.Count > 0 ||
            _groundingReferences.Count > 0)
        {
            context = new AiConversationContext
            {
                Profile = _profile,
                Budget = _budget,
                Tags = _contextTags.Count > 0 ? new Dictionary<string, string>(_contextTags, StringComparer.OrdinalIgnoreCase) : null,
                GroundingReferences = _groundingReferences.Count > 0 ? _groundingReferences.ToList() : null
            };
        }

        var request = new AiChatRequest
        {
            Messages = new List<AiMessage>(_messages),
            Model = _model,
            Options = _options,
            Route = _route,
            Context = context,
            Augmentations = _augmentations.Count > 0
                ? new List<AiAugmentationInvocation>(_augmentations)
                : new List<AiAugmentationInvocation>()
        };

        return request;
    }

    public Task<AiChatResponse> SendAsync(CancellationToken ct = default)
        => _ai.PromptAsync(Build(), ct);

    public Task<AiChatResponse> AskAsync(string message, CancellationToken ct = default)
    {
        WithUser(message);
        return SendAsync(ct);
    }

    public IAsyncEnumerable<AiChatChunk> StreamAsync(CancellationToken ct = default)
        => _ai.StreamAsync(Build(), ct);

    public IAsyncEnumerable<AiChatChunk> StreamAsync(string message, CancellationToken ct = default)
    {
        WithUser(message);
        return _ai.StreamAsync(Build(), ct);
    }

    private AiConversationBuilder AddTextMessage(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Message content is required.", nameof(text));
        _messages.Add(new AiMessage(role, text));
        return this;
    }
}
