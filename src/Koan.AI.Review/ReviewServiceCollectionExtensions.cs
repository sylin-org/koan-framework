using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Review;

/// <summary>
/// Extension methods for registering review queues in DI.
///
/// <code>
/// builder.Services.AddKoanReview(review =>
/// {
///     review.Queue&lt;SupportTicket&gt;("ai-response-quality", q => q
///         .Where(t => t.AiResponse != null &amp;&amp; t.ReviewStatus == ReviewStatus.Pending)
///         .Display(t => new { t.Question, t.AiResponse })
///         .Approve()
///         .Reject(requireReason: true)
///         .Edit(t => t.AiResponse));
/// });
/// </code>
/// </summary>
public static class ReviewServiceCollectionExtensions
{
    /// <summary>
    /// Register Koan review queues.
    /// </summary>
    public static IServiceCollection AddKoanReview(
        this IServiceCollection services,
        Action<ReviewRegistrationBuilder> configure)
    {
        var registry = new ReviewQueueRegistry();
        var builder = new ReviewRegistrationBuilder(registry);
        configure(builder);

        services.AddSingleton(registry);

        return services;
    }
}

/// <summary>
/// Fluent builder for registering review queues during DI setup.
/// </summary>
public sealed class ReviewRegistrationBuilder
{
    private readonly ReviewQueueRegistry _registry;

    internal ReviewRegistrationBuilder(ReviewQueueRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Register a review queue for an entity type.
    /// </summary>
    public ReviewRegistrationBuilder Queue<T>(
        string name,
        Action<ReviewQueueBuilder<T>> configure) where T : IReviewable
    {
        var builder = new ReviewQueueBuilder<T>(name);
        configure(builder);
        var queue = builder.Build();
        _registry.Register(queue);
        return this;
    }
}

/// <summary>
/// Fluent builder for configuring a single review queue.
/// </summary>
public sealed class ReviewQueueBuilder<T> where T : IReviewable
{
    private readonly string _name;
    private System.Linq.Expressions.Expression<Func<T, bool>>? _filter;
    private System.Linq.Expressions.Expression<Func<T, object>>? _display;
    private readonly List<ReviewAction<T>> _actions = [];

    internal ReviewQueueBuilder(string name) => _name = name;

    public ReviewQueueBuilder<T> Where(
        System.Linq.Expressions.Expression<Func<T, bool>> filter)
    {
        _filter = filter;
        return this;
    }

    public ReviewQueueBuilder<T> Display(
        System.Linq.Expressions.Expression<Func<T, object>> display)
    {
        _display = display;
        return this;
    }

    public ReviewQueueBuilder<T> Approve()
    {
        _actions.Add(Review.Approve<T>());
        return this;
    }

    public ReviewQueueBuilder<T> Reject(bool requireReason = false)
    {
        _actions.Add(Review.Reject<T>(requireReason));
        return this;
    }

    public ReviewQueueBuilder<T> Edit(
        System.Linq.Expressions.Expression<Func<T, object>> field)
    {
        _actions.Add(Review.Edit(field));
        return this;
    }

    public ReviewQueueBuilder<T> Label(
        System.Linq.Expressions.Expression<Func<T, object>> field,
        params object[] options)
    {
        _actions.Add(Review.Label(field, options));
        return this;
    }

    public ReviewQueueBuilder<T> Flag(params string[] flagTypes)
    {
        _actions.Add(Review.Flag<T>(flagTypes));
        return this;
    }

    internal ReviewQueue<T> Build()
    {
        return new ReviewQueue<T>
        {
            Name = _name,
            EntityType = typeof(T),
            Filter = _filter ?? throw new InvalidOperationException(
                $"Review queue '{_name}' must have a Where() filter."),
            DisplayFields = _display ?? throw new InvalidOperationException(
                $"Review queue '{_name}' must have a Display() projection."),
            Actions = _actions.AsReadOnly()
        };
    }
}
