# Sylin.Koan.Data.Backup technical contract

## Responsibility

The package owns one operational capability: a provider-bounded, single-Entity archive and its integrity-first
upsert recovery path. `DataBackupModule` registers one scoped `IBackupService` automatically. Standard DI replacement
before module composition remains the customization seam.

It does not own scheduling, whole-application inventory, retention, authorization, HTTP, provider election, schema
migration, encryption, or transactions.

## Archive creation

1. Validate the name and positive page size.
2. Resolve the source partition from `BackupRequest.Partition` or the current `EntityContext`.
3. Read every record through `Data<TEntity,TKey>.BulkRead(pageSize)` into `entity.jsonl` in a temporary ZIP while
   computing SHA-256. Strategy is selected before the first record and recorded as a `koan.data.stream.execution`
   fact (DATA-0113): a provider advertising bounded paging is streamed a page at a time; a provider that does not
   — InMemory, JSON, Redis — is read with one explicitly materialized query, because those keep the whole set
   resident anyway and refusing would make backup untryable on the pillar's floor adapter.
4. Write `manifest.json` with format version (2), collision-proof archive ID, stable assembly/type identities, source
   partition, record count, data entry, checksum, and the scope: one entry per hard segmentation dimension bound for
   the Entity at creation (the tenant), each value a SHA-256 digest of the dimension and its value. Binding refuses a
   required dimension that is missing, as the Entity read would.
5. Close the ZIP and publish the complete seekable file through host-scoped `IStorageService.Put` in the `backups`
   container. The returned `BackupReceipt` includes logical-data integrity and provider object evidence.
6. Remove temporary storage in `finally`. Cleanup is best-effort and never replaces the operation result.

No complete archive is retained in memory. The selected provider's `Put` semantics govern physical publication; the
package supplies a complete closed archive and never publishes its working file while records are still being read.

A materialized source read (step 3) does hold the matching record set in memory once. That is bounded by the
adapter's own residency rather than by this package, and it never applies to a stream-qualified provider.

## Restore failure ordering

Restore downloads the object to a fresh temporary file and completes a validation pass before entering the target
partition or calling `UpsertMany`:

- ZIP and manifest must parse;
- format version (1 or 2), Entity identity, and key identity must match the generic operation;
- the declared fixed data entry must exist;
- every non-empty JSON line must deserialize to the current Entity type; and
- observed count and logical SHA-256 must match the manifest.

Any failure throws `InvalidDataException` and zero records are mutated. The archive's scope must then equal the scope
bound now, or the restore throws `InvalidOperationException` before any write: archives are stored host-scoped and a
restore writes under the current tenant, so restoring tenant A's archive under tenant B would copy A's rows into B
(NK-084). A format 1 archive recorded no scope and restores without the override only where nothing is bound.
`RestoreRequest.AcrossScopes = true` is the administrative override for a deliberate copy or a checked legacy archive. A second pass deserializes validated records
into bounded batches and applies `Data<TEntity,TKey>.UpsertMany`. The target is
`RestoreRequest.TargetPartition ?? manifest.SourcePartition`.

Once the first upsert begins, adapter errors and cancellation can leave an honest partial restore because Koan does
not claim a transaction spanning all batches. Reapplying the same archive is the recovery path for ID-stable Entity
models.

## Public surface

- `IBackupService` — the single operation owner;
- `BackupRequest` / `BackupReceipt` — source/storage/resource intent and publication evidence; and
- `RestoreRequest` / `RestoreReceipt` — target/resource intent and applied recovery evidence.

Archive manifest, codec, naming, storage constants, and implementation remain internal. There is no manual registrar,
static facade, model decoration, adapter optimization SPI, discovery service, or projection contract.
