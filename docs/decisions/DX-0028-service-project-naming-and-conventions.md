---
id: DX-0028
slug: DX-0028-service-project-naming-and-conventions
domain: DX
status: Accepted
date: 2025-08-17
title: Service project naming and conventions (Koan.Service.*)
---
 
# 0028: Service project naming and conventions (Koan.Service.*)

Owners: Koan Core

## Context

We’ve introduced standalone HTTP services (e.g., Inbox over Redis). To avoid naming impedance with client libraries and adapters, we need a clear naming scheme for service runtimes/images.

## Decision

- Prefix all standalone services with `Koan.Service.*` in source/assemblies.
  - Example: `Koan.Service.Inbox.Connector.Redis`.
- Docker/K8s image/runtime names use hyphenated form:
  - Example image/app name: `Koan-service-inbox-redis`.
- Client libraries remain under capability domains:
  - Example: `Koan.Messaging.Inbox.Connector.Http`.
- Apply this policy retroactively to existing services and prospectively to new ones.

## Rationale

- Separates service runtimes from client/adapter libraries.
- Reduces confusion in solutions and dependency graphs.
- Scales to future services (publisher gateway, health announcer, etc.).

## Consequences

- `Koan.Inbox.Redis` is renamed to `Koan.Service.Inbox.Connector.Redis`.
- Docs, samples, and compose files should reference `Koan-service-*` images.
- Build, publish, and discovery scripts should be updated to new names.

## Follow-ups

- Update any Docker Compose and sample references to the new image naming.
- Add service discovery announce for Inbox service.

