# Database bootstrap and baseline adoption

FinViet uses immutable, embedded DbUp migrations. The application runs pending scripts during startup, serializes initialization with a PostgreSQL advisory lock, and records completed scripts in `public.schema_versions`.

## Requirements

- PostgreSQL with the `pgcrypto` and `vector` (pgvector) extensions available.
- A database role that can create application schema objects.
- `ConnectionStrings:DefaultConnection` configured through user-secrets or `ConnectionStrings__DefaultConnection`.
- `Gemini__ApiKey` and other normal production settings configured as described in [gemini-setup.md](gemini-setup.md).
- When a non-Development database has no administrator, `Admin__DefaultPassword` must be at least 12 characters. Optional identity settings are `Admin__DefaultUsername` and `Admin__DefaultEmail`.
- On Render, set `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` to avoid exhausting the platform's inotify instance limit.

Do not put connection strings, passwords, provider credentials, database dumps, or Data Protection key files in Git or the Docker build context.

## Fresh empty database

1. Create an empty PostgreSQL database.
2. Enable `pgcrypto` and `vector` if the provider does not allow the application role to install extensions. Render PostgreSQL supports these extensions, but availability and privileges should be confirmed on staging first.
3. Configure the connection string and required application secrets.
4. Start the API once:

   ```bash
   dotnet run --project src/FinViet.Api
   ```

Startup executes:

1. `V0001__baseline_schema.sql` — canonical schema;
2. `V0002__baseline_reference_data.sql` — three buckets and 18 system categories;
3. `V0003__reconcile_skipped_runtime_schema.sql` — active runtime schema omitted by the former initializer.

Then the application validates the critical schema and seeds one administrator if none exists. Demo customers, wallets, and transactions are Development-only and can be disabled there with `Database__SeedDemoData=false`.

Verify the journal:

```sql
SELECT scriptname, applied
FROM public.schema_versions
ORDER BY scriptname;
```

A second API start must apply no scripts and must not duplicate reference or seed data.

## Adopt a restored full database

Use this only after restoring a reviewed full FinViet backup that already contains the V0001/V0002 schema and reference data but has no `public.schema_versions` table. Normal startup deliberately refuses to infer this state.

1. Take a new verified full backup before adoption.
2. Point the application at the restored database.
3. Stop normal API instances so the one-shot command is the only initializer.
4. Run:

   ```bash
   dotnet run --project src/FinViet.Api -- \
     --adopt-database-baseline \
     --confirm-adopt-baseline \
     --confirm-database-backup
   ```

The command:

- requires both confirmation flags;
- rejects an existing journal;
- validates required tables, extensions, enum labels, `vector(768)`, the HNSW cosine index, buckets, and categories;
- journals V0001 and V0002 without executing their DDL/data statements;
- executes V0003 and any later pending migrations normally;
- validates the resulting schema and seeds only missing environment-appropriate accounts;
- exits without starting the HTTP server.

It refuses partial or drifted schemas. Do not bypass the fingerprint check by manually inserting journal rows.

## Backup and restore

Use PostgreSQL client tools matching or newer than the server where practical. Keep artifacts outside the repository.

Full custom-format backup:

```bash
pg_dump --format=custom --no-owner --no-privileges \
  --dbname="$CONNECTION_STRING" \
  --file=/secure/path/finviet-full.dump
```

Schema-only review artifact:

```bash
pg_dump --schema-only --no-owner --no-privileges \
  --dbname="$CONNECTION_STRING" \
  --file=/secure/path/finviet-schema.sql
```

Restore into a new empty database, never over an active production database:

```bash
pg_restore --clean --if-exists --no-owner --no-privileges \
  --dbname="$TARGET_CONNECTION_STRING" \
  /secure/path/finviet-full.dump
```

After restoring a full pre-journal database, use the explicit adoption command above. After restoring a database whose backup already contains a valid `schema_versions` table, start normally and let DbUp apply only later pending scripts.

## Render staging rehearsal

Before production, rehearse both paths against separate disposable Render PostgreSQL databases:

1. **Empty bootstrap:** enable extensions if required, configure secrets, deploy once, inspect all three journal rows, redeploy, and confirm the second start is a no-op.
2. **Full restore adoption:** restore a local full backup, run the one-shot adoption command, inspect the journal and row counts, then deploy the normal web service.

For each rehearsal:

- compare business-table row counts;
- inspect enum labels and critical constraints/indexes;
- verify `rag_chunk.embedding` is `vector(768)`;
- verify `ix_rag_chunk_embedding` uses HNSW with `vector_cosine_ops`;
- call `/health` and read-only endpoints;
- run the real-server integration suite against staging only.

## Production cutover and rollback

1. Enter a maintenance/read-only window.
2. Take and verify a final full database backup.
3. Restore/bootstrap the target and verify its journal before switching traffic.
4. Deploy one application instance first; verify `/health` and read-only operations.
5. Perform one isolated write smoke test, then switch the frontend.

Rollback uses the previous application image together with the verified database backup. Do not down-migrate, delete journal rows, or dual-write between old and new databases.

## Data Protection and SePay tokens

PostgreSQL backup/restore does not include the ASP.NET Core Data Protection key ring. Existing encrypted SePay access/refresh tokens can only be decrypted if the same persistent key ring and `FinViet` application name are retained. Transfer the protected key storage through the deployment platform's secret/persistent-volume process, or require customers to relink SePay accounts after cutover.

## Future migrations

- Name scripts `VNNNN__description.sql` with the next zero-padded number.
- Never edit or rename a migration after it has reached any shared environment.
- Put mandatory stable reference data changes in SQL with explicit keys and idempotent conflict handling where appropriate.
- Keep user/business/demo data out of migration SQL.
- Test first run, second run, and rollback behavior on disposable PostgreSQL before staging.
- Run the database regression suite with a privileged maintenance connection capable of creating disposable databases and enabling extensions:

  ```bash
  FINVIET_TEST_ADMIN_CONNECTION='Host=localhost;Database=postgres;Username=postgres;Password=...' \
    dotnet test tests/FinViet.Infrastructure.IntegrationTests
  ```

  The suite self-skips when this variable is absent. It covers clean/repeat bootstrap, concurrent initialization, production admin-secret enforcement, and Development demo gating.
- Switching embedding models still requires the separately confirmed RAG re-index procedure in [gemini-setup.md](gemini-setup.md); schema migration does not make embeddings semantically compatible.
