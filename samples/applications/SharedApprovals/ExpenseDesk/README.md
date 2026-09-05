# Expense desk

Submit an employee expense with its receipt, approve it, then record reimbursement.

First run `pwsh samples/applications/SharedApprovals/prepare-framework.ps1` from this checkout.
The [workspace guide](../README.md) explains the local Core repair prerequisite.

```powershell
dotnet run --project samples/applications/SharedApprovals/ExpenseDesk -- --urls http://127.0.0.1:5102
```

Open the loopback URL in a browser. This application has its own SQLite file and consumes the
same foundation as ApprovalDesk. It owns employee, receipt number, and the final reimbursement
timestamp. Reimbursement is an internal record; no money moves or payment service is invoked.

Read `Domain/ExpenseClaim.cs`, `Initialization/ExpenseModule.cs`, and
`Web/ExpensesController.cs`. HTTP CRUD lives at `/api/expenses`, with
`POST /api/expenses/{id}/approve` and `POST /api/expenses/{id}/reimburse` for business actions.
The shared lifecycle rule governs approval even through direct Entity upserts.

See the [workspace guide](../README.md) for the package-update proof, SQLite configuration,
local operating limits, and instructions for coding agents.
