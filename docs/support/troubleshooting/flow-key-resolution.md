# Flow Key Resolution Troubleshooting

## Problem
Entities with [AggregationKey] attributes parked with NO_KEYS due to case mismatch between C# property and JSON serialization.

## Root Cause
- C# property uses PascalCase (e.g., Serial).
- JSON serialization converts to camelCase (e.g., serial).
- Key extraction logic expects PascalCase, causing resolution failure.

## Solution
- Ensure key extraction logic matches JSON serialization casing.
- Update transport envelope creation to handle case consistently.

See also: [Flow Messaging Architecture Guide](../../guides/flow/flow-messaging-architecture.md).
