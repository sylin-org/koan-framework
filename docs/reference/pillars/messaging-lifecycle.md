## Messaging Topology Lifecycle

Contract (Plan → Diff → Apply):
- Inputs: registered handlers, explicit message registrations, configuration, naming strategy.
- Outputs: applied broker objects, diagnostics events, persisted plan hash.
- Error: unrecoverable broker failure or conflicting definition (durability mismatch) halts startup unless override flag allows.

### Modes
| Mode | Description | Destructive |
|------|-------------|-------------|
| Off | Skip lifecycle | No |
| DryRun | Plan & diff only | No |
| CreateIfMissing | Add missing objects | No |
| ReconcileAdditive | Add objects & bindings; never delete | No |
| ForceRecreate | Drop & recreate all derived objects | Yes |

### Lifecycle Steps
1. **Discovery** – Gather message/primitive types & handler groups.
2. **Derivation** – Build desired model: `DesiredTopology`.
3. **Inspection** – Query provider for `CurrentTopology` (might be partial for some brokers).
4. **Diff** – Compute `TopologyDiff` (adds, updates, destructive changes).
5. **Decision** – Respect `ProvisioningMode` & safety flags.
6. **Apply** – Call provider `ITopologyProvisioner` methods.
7. **Emit Diagnostics** – Structured log/OTel event with diff summary & hash.
8. **Cache Hash** – Store hash (e.g., in memory + optional file) to quick-skip next startup if unchanged.

### Hashing Strategy
Stable ordering + JSON canonicalization of desired objects; ignore opaque provider properties (timestamps, auto-generated identifiers).

### Diff Classification
| Change | Category | ForceRecreate Needed |
|--------|----------|----------------------|
| Missing queue/exchange | Additive | No |
| Missing binding | Additive | No |
| Durability mismatch | Breaking | Yes |
| Different DLQ linkage | Breaking | Yes |
| Alias renamed (pattern change) | Potential Breaking | Usually |

### Dry Run Example Output (conceptual)
```json
{
  "bus": "rabbit",
  "mode": "DryRun",
  "adds": {
    "exchanges": ["Koan.commands", "Koan.announcements"],
    "queues": ["cmd.payment.process-order.q.workers"],
    "bindings": ["cmd.payment.process-order -> cmd.payment.process-order.q.workers"]
  },
  "breaking": [],
  "hash": "ac72c6d8"
}
```

### Provider Responsibilities
| Responsibility | Detail |
|----------------|-------|
| Report topology | Return best-effort current objects |
| Provision safely | Use idempotent declarations; avoid throwing on existing identical objects |
| Surface capabilities | Delay/DLQ/native batching in `IMessagingCapabilities` |
| Respect cancellation | Honor `CancellationToken` for fast shutdown |

### Advanced Customization
Swap `ITopologyNaming` or decorate `ITopologyProvisioner` for organizational conventions (prefixes, tenancy suffix). Avoid direct string concatenation elsewhere.

### Operational Guidance
- Run `DryRun` in CI; persist JSON artifact; diff with main branch to detect unreviewed topology changes.
- Use `ForceRecreate` only during controlled maintenance windows.
- Monitor DLQ growth; topology planner does not auto-rescue messages.

### References
- decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md
- decisions/MESS-0071-messaging-dx-and-topology-provisioning.md
