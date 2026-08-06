namespace Koan.Data.Core.Polymorphism;

/// <summary>
/// Carries an explicit typed point-read target through cache and adapter serialization without changing provider APIs.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class EntityMaterializationScope
{
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    public static Type? TargetFor(Type rootType)
    {
        for (var frame = CurrentFrame.Value; frame is not null; frame = frame.Parent)
        {
            if (frame.RootType == rootType)
            {
                return frame.TargetType;
            }
        }

        return null;
    }

    public static IDisposable Enter(Type rootType, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(rootType);
        ArgumentNullException.ThrowIfNull(targetType);
        if (!rootType.IsAssignableFrom(targetType))
        {
            throw new InvalidOperationException(
                $"Materialization target '{targetType.FullName}' does not belong to root '{rootType.FullName}'.");
        }

        var prior = CurrentFrame.Value;
        var frame = new Frame(rootType, targetType, prior);
        CurrentFrame.Value = frame;
        return new Lease(frame, prior);
    }

    private sealed record Frame(Type RootType, Type TargetType, Frame? Parent);

    private sealed class Lease(Frame frame, Frame? prior) : IDisposable
    {
        private Frame? _frame = frame;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _frame, null);
            if (current is null)
            {
                return;
            }

            if (!ReferenceEquals(CurrentFrame.Value, current))
            {
                throw new InvalidOperationException(
                    "Entity materialization scopes must be disposed in reverse order.");
            }

            CurrentFrame.Value = prior;
        }
    }
}
