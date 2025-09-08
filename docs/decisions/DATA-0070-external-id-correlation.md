# DATA-0070: External ID Correlation Framework

## Executive Summary
Framework-level external ID correlation for Sora Flow entities enables automatic cross-system entity tracking and parent-child resolution.

## Problem Statement
- No automatic external ID population in canonical models.
- No cross-system correlation or indexed parent-child resolution.

## Solution
- Populate identifier.external.{source}:{id} in canonical projections.
- Create indexed key relationships for parent-child resolution.
- Merge data from different systems under unified canonical entities.

References: [Flow Messaging Status](../engineering/flow-messaging-status.md), [Flow Messaging Architecture Guide](../guides/flow/flow-messaging-architecture.md).
