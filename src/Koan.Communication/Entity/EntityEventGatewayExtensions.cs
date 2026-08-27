using Koan.Communication;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Data.Core.Model;

/// <summary>
/// Delivers the type-scoped <c>Note.EventGateway.On&lt;OrderShipped&gt;(handler)</c> via a C# 14
/// static extension member - every Entity kind gains it when <c>Sylin.Koan.Communication</c> is
/// referenced, and it is absent without it (Reference = Intent). Thin router over
/// <see cref="EventSubscriptionRegistry"/>. The instance-side <c>order.Events.Raise</c> is the
/// complementary raise operation.
/// </summary>
public static class EntityEventGatewayExtensions
{
    extension<T>(T) where T : Entity<T>
    {
        /// <summary>The type-scoped event gateway: subscribe to business events on this Entity kind
        /// with inline lambdas instead of handler classes. Must be called before the host starts.</summary>
        public static EventGateway<T> EventGateway => default;
    }
}
