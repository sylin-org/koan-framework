# Flow Messaging Implementation Plan

## Executive Summary

Framework-level implementation for Flow messaging in Koan.Messaging/Koan.Flow to provide clean developer experience and dedicated queue routing.

## Requirements & Gaps

- Strong-typed models with [FlowAdapter] detection.
- entity.Send() pattern.
- MessagingInterceptors for envelope wrapping.
- Dedicated FlowEntity queue.
- Orchestrator pattern via [FlowOrchestrator].
- Metadata separation.

## Phased Implementation

- Phase 1: Koan.Messaging enhancement.
- Phase 2: Queue architecture refactor.
- Phase 3: Orchestrator pattern implementation.
- Phase 4: Metadata handling improvements.

References: [Flow Messaging Status](flow-messaging-status.md), [Flow Messaging Refactor ADR](../decisions/WEB-0060-flow-messaging-refactor.md).
