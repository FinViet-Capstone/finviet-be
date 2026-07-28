# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FinViet is a personal finance management API: .NET 8 / ASP.NET Core Web API backend (`FinViet.Api`) over PostgreSQL, with an AI feature suite (transaction categorization, weekly reports, RAG chat) speaking an OpenAI-compatible API (Ollama locally, or a hosted provider), plus SePay bank-account linking.

## Commands

```bash
dotnet restore FinViet.sln
dotnet build FinViet.sln
dotnet run --project src/FinViet.Api
```

Development defaults: API at `http://localhost:5122`, Swagger UI at `/swagger` (paste only the raw `accessToken`, no `Bearer ` prefix — Swagger adds it). Requires `ConnectionStrings:DefaultConnection` (a PostgreSQL instance) configured via `dotnet user-secrets` (see `UserSecretsId` in `FinViet.Api.csproj`) — there's no connection string checked in. Migrations and seed data run automatically on every startup (see Database section) — no separate migrate step.

### Tests

```bash
dotnet test tests/FinViet.Domain.UnitTests
dotnet test tests/FinViet.Application.UnitTests
dotnet test tests/FinViet.Application.UnitTests --filter "FullyQualifiedName~CategoryServiceTests"
dotnet test tests/FinViet.Application.UnitTests --filter "FullyQualifiedName~CategoryServiceTests.Admin_SettingCategoryBucket_Returns403"
```

Integration tests (`tests/FinViet.Api.IntegrationTests`) hit a **real running server**, not `WebApplicationFactory` — start the API first (`dotnet run --project src/FinViet.Api`), then:

```bash
dotnet test tests/FinViet.Api.IntegrationTests
```

If the server isn't reachable, the whole suite self-skips (via `SkippableFact`/`Skip.IfNot(Fx.ServerUp, ...)`) rather than failing. Target/credentials are overridable via env vars (`FINVIET_TEST_BASEURL`, `FINVIET_TEST_CUST_EMAIL`, `FINVIET_TEST_CUST_PASSWORD`, `FINVIET_TEST_ADMIN_USER`, `FINVIET_TEST_ADMIN_PASSWORD`); defaults assume the seeded demo customer/admin exist on that instance.

### RAG re-index (destructive-adjacent — confirm with user before running)

```bash
dotnet run --project src/FinViet.Api -- --reindex-rag --confirm-reindex
```

Regenerates all `rag_chunk` embeddings in place (needed after switching embedding models/providers — vectors from different models aren't semantically compatible even at the same dimension). Refuses to run without `--confirm-reindex`, and refuses if `Ai:RagEnabled=true` (must be `false` during re-index). See [docs/ollama-setup.md](docs/ollama-setup.md) for the full local-Ollama re-index runbook, including a `pg_dump` backup step.

## Architecture

Four projects, referenced top-to-bottom (`Api → Infrastructure/Application → Domain`):

- **`FinViet.Api`** — `Controllers/`, `Middlewares/`, `Common/`, `wwwroot/` (static avatars). `Filters/` is currently empty — cross-cutting concerns go through MediatR pipeline behaviors or middleware instead of action filters.
- **`FinViet.Application`** — CQRS layer. `Features/{Feature}/Commands|Queries/`, `DTOs/{Feature}/`, `Interfaces/` (repository/service contracts, `I`-prefixed), `Behaviors/` (MediatR pipeline behaviors), `Common/Exceptions/` (app-level exceptions, current), `Exceptions/` (legacy, see Error Handling below).
- **`FinViet.Infrastructure`** — EF Core, external services, and **most MediatR handlers** (even though handlers logically belong to Application, they're registered from here — see `Program.cs` comment on `AddMediatR`). `Persistence/Entities/` (EF-scaffolded partial classes), `Persistence/Repositories/`, `Persistence/Context/`, `Persistence/Configurations/`, `Persistence/Migrations/` (raw versioned SQL, see Database below), `ExternalServices/{Provider}/` (SePay, OpenAiCompatible, Payment, Notification, Documents, TransactionImport), `Services/`, `Services/Background/`, `Features/{Feature}/Commands|Queries/{CommandName}/{CommandName}Handler.cs`.
- **`FinViet.Domain`** — currently just `Enums/`. EF entities live in `Infrastructure/Persistence/Entities`, not here — an intentional, known deviation from "pure" Clean Architecture (pragmatic choice for an EF-scaffolded schema), not a bug to fix incidentally. `Entities/`/`Exceptions/`/`ValueObjects/` folders exist but are empty.

Services are registered from `AddApplicationServices()` (Application) and `AddInfrastructureServices(config)` (Infrastructure), both called from `Program.cs`. MediatR is told to scan the **Infrastructure** assembly because that's where most handlers live.

### Database — raw SQL migrations, not EF Core Migrations

There is no `dotnet ef migrations` workflow. Schema changes are hand-written, numbered SQL files in `Infrastructure/Persistence/Migrations/` (`V{n}__description.sql`), applied in order by `DbInitializer.ApplyMigrationsAsync` on every app startup (idempotent — `IF NOT EXISTS`/`ON CONFLICT`). `DbInitializer` also runs an `EnsureAdditiveTablesAsync` step for tables added after the externally-provisioned "v3" schema baseline, then seeds admin/demo data. Read `DbInitializer.cs` before adding a migration — it has version-detection logic (`IsV3SchemaAppliedAsync`, `IsCategoryTransactionV21AppliedAsync`) that skips old migrations once certain schema markers are already present; new migration files must be numbered above the current max and safe to no-op on re-run.

PostgreSQL enums are mapped to CLR enums via an `NpgsqlDataSourceBuilder` in `Infrastructure/DependencyInjection.cs` (`MapEnum<T>("pg_enum_name")`) — most entity columns actually expose these as `string` with a `PgEnumStringConverter` EF value converter binding them back to the mapped CLR enum, so Npgsql sends the enum OID instead of text. If you add a new Postgres enum type, register it in `DependencyInjection.cs` before it's used in the DbContext, and know that a plain `AddDbContext` call elsewhere would silently outrank this via `TryAdd` — don't add one.

### CQRS / MediatR pattern

Two request-organization styles both exist in the codebase; match whatever a feature already uses rather than converting it:

1. **One file per command/query with co-located handler subfolder** (newer — Auth, Account, Profile, Categories):
   ```
   Features/Auth/Commands/Login/LoginCommand.cs        (Application, record : IRequest<TResponse>)
   Features/Auth/Commands/Login/LoginCommandHandler.cs (Infrastructure)
   ```
2. **Flat multi-command file per feature** (older — Transactions): all commands as mutable classes in one `{Feature}Commands.cs`, all handlers in one `{Feature}Handlers.cs`.

Validation is FluentValidation as a MediatR pipeline behavior (`Application/Behaviors/ValidationBehavior.cs`) — don't hand-validate inside handlers. Validators live next to their command (`LoginCommandValidator.cs`) and throw FluentValidation's `ValidationException`, mapped to 400 by the global middleware.

### Error handling

One global exception middleware (`Api/Middlewares/ExceptionHandlingMiddleware.cs`, registered first in the pipeline) maps exception types to HTTP status. Don't add local try/catch-and-format-response blocks in controllers — throw a typed exception instead:

| Exception | Status | Notes |
|---|---|---|
| `FluentValidation.ValidationException` | 400 | auto-thrown by `ValidationBehavior` |
| `BadRequestException` | 400 | |
| `UnauthorizedException` / `UnauthorizedAccessException` | 401 | |
| `ForbiddenException` | 403 | |
| `NotFoundException` | 404 | ctor overload `new NotFoundException("Wallet", walletId)` |
| `ConflictException` | 409 | |
| `BusinessRuleException` | 422 | carries an optional `Code` (e.g. `"insufficient_balance"`) the FE maps to a localized message — always set it for anything the FE needs to branch on |
| `ExternalServiceException` | 502 | upstream provider failure (SePay, AI provider, ...) |
| `IntegrationUnavailableException` | 503 | integration not configured |
| anything else | 500 | stack trace only when `IsDevelopment()` |

New app-level exceptions go in `Application/Common/Exceptions/AppExceptions.cs`. There's also a legacy `Application/Exceptions/` namespace still handled by the middleware for back-compat — prefer `Common/Exceptions` for new code.

### Data access

- Repositories wrap `FinVietDbContext` directly (constructor-injected), each implementing a hand-written `I{Name}Repository` from `Application/Interfaces` — no generic repository base.
- `.AsNoTracking()` for read-only queries; `CancellationToken cancellationToken = default` threaded through every async repository/service method.
- Row locking for balance-mutating operations uses `FromSqlInterpolated` with `FOR UPDATE` inside an explicit `_context.Database.BeginTransactionAsync()` (see `TransactionRepository.LockWalletsAsync`) — follow this for anything mutating wallet balances or similar contended state, not optimistic-concurrency checks.
- Idempotent mutating endpoints (transaction creation, saving-goal create/contribute, wallet transfer/withdraw) go through `IdempotencyStore` (`Infrastructure/Persistence/Idempotency`), keyed by an `Idempotency-Key` header — replay the stored response instead of re-executing on a repeated key.
- Pagination uses shared `Application/Common/PagedResult<T>` (`Page`, `PageSize`, `TotalItems`, `TotalPages`, `Items`) — not per-feature paged DTOs.
- No AutoMapper — mapping is manual, typically a private static `MapToDto(...)` at the bottom of a repository, or a small static `{Feature}DtoMapper` class (see `ProfileDtoMapper`).
- EF entities in `Infrastructure/Persistence/Entities` are scaffolded `partial class`, nullable-annotated, `= null!` for required nav/string props. Extend via the `partial` class for computed properties (see `Transaction.SourceChannel`/`Note`/`BeneficiaryName`, which are back-compat aliases over renamed columns) rather than hand-editing generated-looking sections.

### API response envelope

Standard responses wrap in `ApiResponse<T> = { success, message?, data? }`. `TransactionsController` is the one exception — it returns raw objects/`PagedResult<T>` with no envelope. Full endpoint inventory: [docs/api-reference.md](docs/api-reference.md).

### Controllers

- `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize(Roles = "Customer"|"Admin")]` at the class level when a whole controller is role-restricted.
- Thin controllers: build a command/query, `await _mediator.Send(...)`, return `Ok(...)`. No business logic here.
- Pull the authenticated user id via a small private helper reading `ClaimTypes.NameIdentifier`/`"sub"` (see `TransactionsController.GetCustomerId()`) rather than repeating claim-parsing inline.
- No API versioning — routes are unversioned (`api/[controller]`); don't introduce a versioned route for a single endpoint without discussing it first.

### AI / RAG suite

`Infrastructure/ExternalServices/OpenAiCompatible/` talks to any OpenAI-compatible chat+embedding API, configured via the `Ai:*` section (`BaseUrl`, `ClassificationModel`, `GenerationModel`, `EmbeddingModel`, `EmbeddingDimensions`, `RagEnabled`, ...), validated at startup (`AddOptions<AiOptions>().ValidateOnStart()`). `EmbeddingDimensions` must equal `rag_chunk.embedding`'s pgvector dimension (768, backed by `nomic-embed-text` locally). Local dev typically runs this against Ollama — see [docs/ollama-setup.md](docs/ollama-setup.md) for setup, model pulls, and the re-index runbook. Switching embedding providers/models requires the `--reindex-rag` flow above; vectors from different models aren't compatible even at matching dimensions.

### Background work

Scheduled/recurring jobs implement `BackgroundService` under `Infrastructure/Services/Background/` (see `WeeklyReportScheduler`) — not `IHostedService` directly, not a third-party scheduler.

### Security

- JWT bearer auth (`Jwt:Secret`/`Issuer`/`Audience` from configuration, never hardcoded — `appsettings.Development.json` has a dev-only secret, real environments use user-secrets/env vars).
- CORS is a single named/default policy allowing `AppSettings:FrontendUrl` — don't add `AllowAnyOrigin`.
- `UseHttpsRedirection()` and the global exception middleware run before auth in the pipeline — preserve that order if touching `Program.cs`.

### `tests/test-cases/`

A standalone Node.js tool (not part of the .NET solution) that generates `FinViet_TestCases.docx`/`.xlsx` from `testcases.data.mjs` via `generate_docx.mjs`/`generate_xlsx.mjs`. Run with `node generate_docx.mjs` / `node generate_xlsx.mjs` from that directory if regenerating the test-case documents.

## Workflow (from `context/ai-interaction.md`)

For every feature/fix:

1. **Document** the feature in [context/current-feature.md](context/current-feature.md) first (never delete its guiding HTML comments — clear them per-section when starting a new feature instead).
2. **Branch**: `feature/[name]` or `fix/[name]`.
3. **Implement**.
4. **Test**: verify via Swagger or the integration suite; `dotnet build` must pass; `dotnet test` for anything with coverage.
5. **Iterate** as needed.
6. **Commit** — only after build passes and manual verification succeeds. Conventional commit messages (`feat:`, `fix:`, `chore:`); never mention AI generation in the commit message. Never commit or push without explicit permission.
7. **Merge** to `main`/`khoi`, then **delete the branch**.
8. Mark the feature completed in `context/current-feature.md` and append to its History section.

Additional standing rules:

- Make minimal, scoped changes; don't refactor unrelated code or "fix" known deviations (e.g. the Domain/Infrastructure entity split, or unifying the two CQRS styles) as a side effect of an unrelated task.
- Don't add features beyond what's documented in `context/current-feature.md` for the current cycle.
- If something isn't working after 2-3 attempts, stop and explain rather than continuing to try random fixes.
- `context/coding-standards.md` and `context/current-feature.md` are living documents for this repo — check them for the latest feature-in-progress and any standards updates beyond what's summarized here.
