---
id: DX-0028
slug: DX-0028-service-project-naming-and-conventions
domain: DX
status: Accepted
date: 2025-08-17
title: Service project naming and conventions (Sora.Service.*)
---
 
# 0028: Service project naming and conventions (Sora.Service.*)

Owners: Sora Core

## Context

We’ve introduced standalone HTTP services (e.g., Inbox over Redis). To avoid naming impedance with client libraries and adapters, we need a clear naming scheme for service runtimes/images.

## Decision

- Prefix all standalone services with `Sora.Service.*` in source/assemblies.
  - Example: `Sora.Service.Inbox.Redis`.
- Docker/K8s image/runtime names use hyphenated form:
  - Example image/app name: `sora-service-inbox-redis`.
- Client libraries remain under capability domains:
  - Example: `Sora.Messaging.Inbox.Http`.
- Apply this policy retroactively to existing services and prospectively to new ones.

## Rationale

- Separates service runtimes from client/adapter libraries.
- Reduces confusion in solutions and dependency graphs.
- Scales to future services (publisher gateway, health announcer, etc.).

## Consequences

- `Sora.Inbox.Redis` is renamed to `Sora.Service.Inbox.Redis`.
- Docs, samples, and compose files should reference `sora-service-*` images.
- Build, publish, and discovery scripts should be updated to new names.

## Follow-ups

- Update any Docker Compose and sample references to the new image naming.
- Add service discovery announce for Inbox service.
