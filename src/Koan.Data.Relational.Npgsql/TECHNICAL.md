# Technical notes

Concrete providers own discovery, configuration, identity, and startup reporting. This package owns only their common Npgsql data path and receives every provider decision through an immutable `NpgsqlRepositoryOptions` value.
