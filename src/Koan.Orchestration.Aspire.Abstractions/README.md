# Sylin.Koan.Orchestration.Aspire.Abstractions

Inert Aspire resource-contribution vocabulary for Koan modules.

## Install

Application developers normally reference a functional adapter or `Sylin.Koan.Orchestration.Aspire`; they do not
install this package directly. Module authors can reference the contribution contract without activating Aspire:

```powershell
dotnet add package Sylin.Koan.Orchestration.Aspire.Abstractions
```

## Meaningful result

A module that can describe an Aspire resource implements `IKoanAspireResources` while keeping its own functional
semantics in its own assembly. When the functional Aspire host is also referenced, it discovers those contributors
and asks them to add their resources; without that host, the same contributor code remains inert.

## Guarantees and limits

This package contains no `KoanModule`, performs no discovery, and starts no resource. Referencing it cannot activate
Koan's Aspire runtime.

- Implementing the contract describes a possible resource; it does not enable orchestration.
- The functional `Sylin.Koan.Orchestration.Aspire` package owns contributor discovery and execution.
- Contributors remain responsible for their own resource semantics and must not depend on the functional Aspire
  package merely to share this contract.

See [TECHNICAL.md](TECHNICAL.md) for the activation boundary.
