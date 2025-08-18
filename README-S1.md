# Sora S1 Prep

This milestone adds web surface and relational adapter groundwork.

- Query capability flags via `IQueryCapabilities` and `QueryCapabilities` enum.
- JSON adapter advertises LINQ support.

Next:
- Scaffold Minimal API host and Dapper/SQLite adapter.
- Implement `IStringQueryRepository` for SQL adapters; leave JSON without string queries.
