---
type: REFERENCE
domain: data
title: "SQLite replacement lineage"
status: accepted
last_updated: 2026-07-28
---

# SQLite replacement lineage

The replacement starts from commit `ccebfee938b9ee60c5c29354e015154b0a059f28`. Every former SQLite implementation
file was deleted before new production code was added. Stable public identities and provider facts were retained;
former repository, discovery, mapping, query, schema, source-integration, and object-codec control flow was not.

The result has one repository type and one `MappingPlan`/`RelationalCommandPlanner` execution path for both managed
`Id + Json` storage and explicit legacy mappings. Shared Relational code owns filters, structured values, named SQL,
and neutral records. SQLite code owns only SQLite syntax, connection modes, physical schema facts, and dispatch.

Verification on the replacement tree: SQLite 47/47, Relational 16/16, Data Core 471/471, all with zero skips. The Web
adapter-surface project could not be rebuilt because required packages were absent from the offline cache; its stale
binary was rejected as evidence.
