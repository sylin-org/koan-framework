---
id: DATA-0009
slug: DATA-0009-unify-on-ientity
domain: DATA
status: Accepted
title: Unify on IEntity<TKey>; remove IAggregateRoot<TKey>
---
 
# 0009 — Unify on IEntity<TKey>; remove IAggregateRoot<TKey>
 

## Context
- We originally exposed both IEntity<TKey> and IAggregateRoot<TKey> (which extended IEntity<TKey>).
- Repositories, controllers, and helpers constrained TEntity to IAggregateRoot<TKey>, causing confusion and perceived feature loss for interface-based models.

## Decision
- Make IEntity<TKey> the one interface required for persistence.
- Remove IAggregateRoot<TKey>; all generic constraints switch to IEntity<TKey>.
- Keep base Entity<TEntity> as optional sugar (Id embedding + static conveniences), but ensure all helpers are available for any IEntity model.

## Consequences
- Simpler mental model: implement IEntity<TKey> to participate in data APIs.
- No lost functionality for interface-based models; extensions now target IEntity<TKey>.
- DDD notion of "aggregate root" remains a documentation concept rather than a type.

## Implementation notes
- Updated constraints in abstractions, core, adapters, and web controllers to IEntity.
- Reflection helpers and discovery updated to look for IEntity<>.
- Docs updated (Getting Started, Core Contracts, Architecture Overview) to reflect new naming.

## Migration
- Greenfield: no compatibility layer needed. If any external code referenced IAggregateRoot, replace with IEntity.
