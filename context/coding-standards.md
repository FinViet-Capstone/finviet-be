# FinViet Backend — Coding Standards

These reflect how this codebase actually looks today. When in doubt, match the
nearest existing file in the same folder over anything written here.

## Solution Layout

Four projects, referenced top-to-bottom (`Api → Infrastructure/Application → Domain`):

- **`FinViet.Api`** — `Controllers/`, `Middlewares/`, `Common/`, `Properties/`, `wwwroot/` (static avatars). `Filters/` exists but is currently empty — cross-cutting concerns go through MediatR pipeline behaviors or middleware instead of action filters.
- **`FinViet.Application`** — CQRS layer. `Features/{Feature}/Commands|Queries/`, `DTOs/{Feature}/`, `Interfaces/` (repository/service contracts, `I`-prefixed), `Behaviors/` (MediatR pipeline behaviors), `Common/Exceptions/` (app-level exceptions), `Exceptions/` (legacy — see Exceptions section below).
- **`FinViet.Infrastructure`** — EF Core, external services, and **most MediatR handlers**. `Persistence/Entities/` (EF-scaffolded classes), `Persistence/Repositories/`, `Persistence/Context/`, `Persistence/Configurations/`, `Persistence/Migrations/`, `ExternalServices/{Provider}/` (Finverse, SePay, Gemini, Payment, Notification, Documents), `Services/`, `Services/Background/`, `Features/{Feature}/Commands|Queries/{CommandName}/{CommandName}Handler.cs`.
- **`FinViet.Domain`** — currently just `Enums/`. EF entities live in `Infrastructure/Persistence/Entities`, not here — this is a known, intentional deviation from "pure" Clean Architecture (pragmatic choice for an EF-scaffolded schema), not a bug to fix incidentally.

Register services from `AddApplicationServices()` (Application) and `AddInfrastructureServices(config)` (Infrastructure), both called from `Program.cs`. MediatR is told to scan the **Infrastructure** assembly (`Program.cs`) because that's where most handlers live — see `// NOTE:` comments in `Program.cs` before touching DbContext/MediatR registration.

## CQRS / MediatR Pattern

Two request-organization styles both exist; either is acceptable, follow what a feature already uses:

1. **One file per command/query with co-located handler subfolder** (newer, most features — Auth, Account, Profile):
   ```
   Features/Auth/Commands/Login/LoginCommand.cs        (Application)
   Features/Auth/Commands/Login/LoginCommandHandler.cs (Infrastructure)
   ```
   Requests are `record`s implementing `IRequest<TResponse>`:
   ```csharp
   public record LoginCommand(string Email, string Password) : IRequest<AuthResponseDto>;
   ```

2. **Flat multi-command file per feature** (older, e.g. Transactions):
   ```
   Features/Transactions/Commands/TransactionCommands.cs   (all commands as classes)
   Features/Transactions/Handlers/TransactionHandlers.cs   (all handlers)
   ```
   Requests here are mutable classes with `{ get; set; }` properties, not records.

Validation is FluentValidation, wired as a MediatR pipeline behavior (`Application/Behaviors/ValidationBehavior.cs`) — don't validate manually inside handlers when a validator will do. Validators live next to their command/DTO (e.g. `LoginCommandValidator.cs`) and throw FluentValidation's `ValidationException`, which the global middleware turns into a 400 with per-field errors.

## Naming Conventions

- PascalCase: classes, methods, public members, command/query names (`CreateTransactionCommand`, `GetTransactionsQuery`).
- `_camelCase` for private fields (`_context`, `_mediator`, `_next`).
- Interfaces prefixed `I` (`ITransactionRepository`, `IFirebaseAuthService`).
- Controllers: `{Feature}Controller`, one per feature, route via `[Route("api/[controller]")]`.
- Handlers: `{CommandOrQueryName}Handler`.
- Test methods: `MethodOrScenario_Condition_ExpectedResult` (e.g. `Admin_SettingCategoryBucket_Returns403`).
- File-scoped namespaces (`namespace Foo.Bar;`) are the default for new code; a few older files still use block-scoped `namespace Foo.Bar { }` (e.g. `PagedResult.cs`) — don't mass-convert them incidentally, but write new files file-scoped.

## Error Handling

All exceptions are caught by one global middleware — `Api/Middlewares/ExceptionHandlingMiddleware.cs`, registered first in the pipeline. Don't add local try/catch-and-format-response blocks in controllers; throw a typed exception instead and let the middleware map it:

| Exception | HTTP Status | Notes |
|---|---|---|
| `FluentValidation.ValidationException` | 400 | Auto-thrown by `ValidationBehavior`; per-field errors |
| `BadRequestException` | 400 | |
| `UnauthorizedException` / `UnauthorizedAccessException` | 401 | |
| `ForbiddenException` | 403 | |
| `NotFoundException` | 404 | Ctor overload: `new NotFoundException("Wallet", walletId)` |
| `ConflictException` | 409 | |
| `BusinessRuleException` | 422 | Carries an optional machine-readable `Code` (e.g. `"insufficient_balance"`) that the frontend maps to a localized (VI) message — always set `Code` for anything the FE needs to branch on |
| `ExternalServiceException` | 502 | Upstream provider failure (Finverse, SePay, Gemini, ...) |
| `IntegrationUnavailableException` | 503 | Integration not configured |
| anything else | 500 | Stack trace only included when `IsDevelopment()` |

New app-level exceptions go in `Application/Common/Exceptions/AppExceptions.cs` next to the existing ones. There is also a legacy `Application/Exceptions/` namespace with its own `NotFoundException`/`ValidationException` still handled by the middleware for backward compatibility — prefer `Common/Exceptions` for new code, don't add to the legacy one.

## Data Access

- Repositories wrap `FinVietDbContext` directly (constructor-injected), implementing an `I{Name}Repository` interface from `Application/Interfaces`. No generic repository base class — each repository is hand-written for its aggregate.
- Use `.AsNoTracking()` for read-only queries.
- Use `CancellationToken cancellationToken = default` on every async repository/service method and pass it through to EF calls.
- Row locking for balance-mutating operations uses `FromSqlInterpolated` with `FOR UPDATE` (see `TransactionRepository.LockWalletsAsync`) inside an explicit `_context.Database.BeginTransactionAsync()` — follow this pattern for anything that mutates wallet balances or similar contended state, not manual optimistic-concurrency checks.
- Idempotent mutating endpoints (e.g. transaction creation) go through `IdempotencyStore` (`Infrastructure/Persistence/Idempotency`) keyed by an `Idempotency-Key` header — replay a stored response instead of re-executing when a key repeats.
- Pagination uses the shared `Application/Common/PagedResult<T>` (`Page`, `PageSize`, `TotalItems`, `TotalPages`, `Items`), not ad hoc paged DTOs per feature.
- No AutoMapper — mapping between entities and DTOs is manual, typically a private static `MapToDto(...)` method at the bottom of the repository or a small `{Feature}DtoMapper` static class (see `Infrastructure/Features/Profile/ProfileDtoMapper.cs`).
- EF entities in `Infrastructure/Persistence/Entities` are scaffolded: `partial class`, nullable reference types (`string? Foo`), `= null!` for required navigation/string props, `virtual` navigation properties. Don't hand-edit generated-looking sections when regenerating from the DB is an option; extend via the `partial` class if adding computed properties (see `Transaction.SourceChannel`/`Note`/`BeneficiaryName` — back-compat aliases over renamed columns).

## Controllers

- `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize(Roles = "...")]` at the class level when a whole controller is role-restricted.
- Thin controllers: build a command/query from the request, `await _mediator.Send(...)`, return `Ok(...)`. No business logic in controllers.
- Pull the authenticated user id via a small private helper reading `ClaimTypes.NameIdentifier`/`"sub"` (see `TransactionsController.GetCustomerId()`) — don't repeat claim-parsing inline across actions.
- No API versioning is implemented (no `Asp.Versioning`) — the API is unversioned (`api/[controller]`). Don't introduce versioned routes for a single endpoint without discussing it first, since it'd be inconsistent with everything else.

## Security

- JWT bearer auth configured in `Program.cs` (`Jwt:Secret`/`Jwt:Issuer`/`Jwt:Audience` from configuration — never hardcode). `[Authorize(Roles = "Customer")]` / `"Admin"` gates controllers.
- CORS is a single named/default policy allowing `AppSettings:FrontendUrl` — don't add `AllowAnyOrigin`.
- `UseHttpsRedirection()` and global exception middleware run before auth in the pipeline; keep that order if editing `Program.cs`.

## API Documentation

- Swagger/Swashbuckle is configured with a JWT bearer security scheme (paste raw token, no `Bearer ` prefix needed).
- XML `<summary>` comments exist on some controllers/DTOs but `GenerateDocumentationFile` is not enabled in any `.csproj`, so they currently don't reach Swagger UI. Adding comments is fine; don't assume they're visible in Swagger until that's turned on.

## Background Work

Scheduled/recurring jobs implement `BackgroundService` under `Infrastructure/Services/Background/` (see `WeeklyReportScheduler.cs`), not `IHostedService` directly and not a third-party scheduler.

## Testing

- **Unit tests** (`tests/FinViet.*.UnitTests`): xUnit. No mocking library (Moq/NSubstitute) is currently referenced — these projects are mostly placeholder/scaffold today (`UnitTest1.cs`). If you add real unit tests that need mocking, add the package rather than hand-rolling fakes, but confirm the direction first since it's a net-new pattern here.
- **Integration tests** (`tests/FinViet.Api.IntegrationTests`): xUnit + `SkippableFact`, driven against a **real running server**, not `WebApplicationFactory`. Every test calls `RequireServer()` first and uses `Fx.SendAsync(...)` / helper methods (`AdminGet`, etc.) from `ApiTestBase`/`ApiTestFixture`. Tests are organized one class per feature (`TransactionTests`, `WalletTests`, `BudgetTests`, ...), with `// TC-{AREA}-{NN}` comment tags above each test describing the scenario — keep that tagging convention for new tests.
- Don't introduce in-memory `WebApplicationFactory`-style integration tests alongside the live-server style without discussing it — it's a deliberate existing choice, not an oversight.

## General

- `async`/`await` throughout for I/O; no blocking `.Result`/`.Wait()`.
- Nullable reference types are enabled — respect `?` annotations rather than suppressing warnings.
- `record` types for new immutable CQRS commands/queries (style 1 above); plain classes with settable properties for anything following the older Transactions-style pattern already in that file.
- Per [ai-interaction.md](ai-interaction.md): make minimal, scoped changes, preserve existing patterns in the touched feature, and don't refactor unrelated code (e.g. don't "fix" the Domain/Infrastructure entity split or unify the two CQRS styles) as a side effect of an unrelated task.
