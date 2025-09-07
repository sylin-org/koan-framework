using System;
using System.Linq;
using Sora.Flow.Attributes;
using Sora.Messaging;

namespace Sora.Flow
{
    public static class FlowMessagingRegistration
    {
        // DEPRECATED: This class is part of the old auto-handler system
        // New implementation uses TransportEnvelope<T> and JSON string messaging
        
        /*
        public static void RegisterTransformers()
        {
            // Old transformer registration - no longer used
        }
        */
    }
}