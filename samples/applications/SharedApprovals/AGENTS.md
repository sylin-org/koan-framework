# Working on Shared Approvals

Read `README.md`, then the affected consumer's README and `Foundation/TECHNICAL.md`.
Keep `AddKoan()` as the host bootstrap and use the Entity's statics/instance operations.

- Shared approval state and spending policy belong in Foundation.
- Purchase ordering belongs in ApprovalDesk; reimbursement belongs in ExpenseDesk.
- Add policy at Entity lifecycle boundaries so ordinary persistence calls cannot bypass it.
- Read the restored foundation README when consuming its NuGet package; it matches that version.
- Verify meaningful changes with `pwsh ./verify.ps1`; inspect actual facts and retained logs.
- Run `pwsh ./prepare-framework.ps1` once in this checkout: the fixture needs its local Core
  manifest repair alongside published Koan capabilities. Read exact packages and recipes from the current
  [Koan capability map](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/capability-map.md).

Its isolated test feed is never a publication target. Package versions are computed from Git;
changing the policy does not authorize editing versions or migrating stored data.
