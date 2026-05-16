using System.Linq.Expressions;

namespace Koan.AI.Review;

/// <summary>
/// Static factory for building review queues and creating review actions.
///
/// <code>
/// var queue = Review.Create&lt;SupportReply&gt;(
///     "support-review",
///     where: r => r.ReviewStatus == ReviewStatus.Pending,
///     display: r => new { r.Id, r.Message, r.Confidence },
///     actions: [Review.Approve&lt;SupportReply&gt;(), Review.Reject&lt;SupportReply&gt;(true)]);
/// </code>
/// </summary>
public static class Review
{
    // ── Queue Factory ──

    /// <summary>Create a typed review queue with filter, display projection, and available actions.</summary>
    public static ReviewQueue<T> Create<T>(
        string name,
        Expression<Func<T, bool>> where,
        Expression<Func<T, object>> display,
        IReadOnlyList<ReviewAction<T>> actions) where T : IReviewable
    {
        return new ReviewQueue<T>
        {
            Name = name,
            EntityType = typeof(T),
            Filter = where,
            DisplayFields = display,
            Actions = actions
        };
    }

    // ── Action Helpers ──

    /// <summary>Create an approve action.</summary>
    public static ApproveAction<T> Approve<T>() => new();

    /// <summary>Create a reject action, optionally requiring a reason.</summary>
    public static RejectAction<T> Reject<T>(bool requireReason = false) => new(requireReason);

    /// <summary>Create an edit action targeting a specific field.</summary>
    public static EditAction<T> Edit<T>(Expression<Func<T, object>> field)
    {
        var fieldName = ExtractFieldName(field);
        return new EditAction<T>(fieldName);
    }

    /// <summary>Create a label action targeting a specific field with fixed options.</summary>
    public static LabelAction<T> Label<T>(Expression<Func<T, object>> field, params object[] options)
    {
        var fieldName = ExtractFieldName(field);
        return new LabelAction<T>(fieldName, options);
    }

    /// <summary>Create a flag action with the given flag types.</summary>
    public static FlagAction<T> Flag<T>(params string[] flagTypes) => new(flagTypes);

    // ── Internal ──

    private static string ExtractFieldName<T>(Expression<Func<T, object>> expression)
    {
        var body = expression.Body;

        // Unwrap Convert (boxing for value types)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        return body switch
        {
            MemberExpression member => member.Member.Name,
            _ => throw new ArgumentException(
                "Expression must be a simple member access (e.g., x => x.FieldName).", nameof(expression))
        };
    }
}
