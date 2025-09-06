using System;
using System.Linq;
using Sora.Flow.Attributes;
using Sora.Messaging;

namespace Sora.Flow
{
    public static class FlowMessagingRegistration
    {
        public static void RegisterTransformers()
        {
            // Register transformer for FlowEntity
            MessagingTransformers.Register("Sora.Flow.Model.FlowEntity", payload =>
            {
                var adapterAttr = payload.GetType().GetCustomAttributes(typeof(FlowAdapterAttribute), true)
                    .FirstOrDefault() as FlowAdapterAttribute;
                return new TransportEnvelope
                {
                    Version = "1",
                    Source = adapterAttr?.System ?? "unknown",
                    Model = payload.GetType().Name,
                    Type = "Sora.Flow.Model.FlowEntity",
                    Payload = payload
                };
            });

            // Register transformer for DynamicFlowEntity
            MessagingTransformers.Register("Sora.Flow.Model.DynamicFlowEntity", payload =>
            {
                var adapterAttr = payload.GetType().GetCustomAttributes(typeof(FlowAdapterAttribute), true)
                    .FirstOrDefault() as FlowAdapterAttribute;
                return new TransportEnvelope
                {
                    Version = "1",
                    Source = adapterAttr?.System ?? "unknown",
                    Model = payload.GetType().Name,
                    Type = "Sora.Flow.Model.DynamicFlowEntity",
                    Payload = payload
                };
            });
        }
    }
}