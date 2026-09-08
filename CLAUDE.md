# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Build the whole solution (JobTracker.slnx)
dotnet build

# Start PostgreSQL (matches the connection string in appsettings.json)
docker compose up -d

# Run the API (http://localhost:5238 / https://localhost:7163)
dotnet run --project src/JobTracker.Api
# Scalar API reference (Development only): /scalar

# Tests
dotnet test
dotnet test --filter "FullyQualifiedName~JobTests"   # single class/test

# EF Core migrations (Persistence holds the model, Api holds the connection string)
dotnet ef migrations add <Name> --project src/JobTracker.Persistence --startup-project src/JobTracker.Api
dotnet ef database update --project src/JobTracker.Persistence --startup-project src/JobTracker.Api
```

`JobsDbContextFactory` provides the design-time context. It ignores `appsettings.json` and reads
`JOBTRACKER_CONNECTION_STRING` (falling back to `postgres/postgres`), so set that env var when
running `dotnet ef` against a non-default database.

NuGet versions are managed centrally in `Directory.Packages.props` — `PackageReference` entries in
`.csproj` files carry no `Version` attribute. Add the version there first.

## Architecture

Clean Architecture with dependencies pointing inward: `Api → Application → Domain`, and
`Persistence → Application + Domain`. The Api references Persistence only to call `AddPersistence`
at startup; it never touches EF types directly.

### Request flow

Controller (`ApiControllerBase`) → `Mediator.Send` → MediatR handler → `IJobRepository` /
`IUnitOfWork` → `JobsDbContext`. Handlers are `internal sealed`; only the command/query records
and response DTOs are public.

### Result pattern, not exceptions, for expected failures

Handlers return `Result` / `Result<T>` carrying an `Error(Code, Description, ErrorType)`.
`ApiControllerBase.ToProblem` maps `ErrorType` → HTTP status (Validation→400, NotFound→404,
Conflict→409, Unauthorized→401, Forbidden→403, else 500). Error codes follow
`Jobs.<Operation>.<Reason>` (e.g. `Jobs.Start.InvalidStatus`).

Handlers catch `DomainException` / `ArgumentException` from the aggregate and convert them into
`Result.Failure`. `ApiExceptionFilterAttribute` is the last-resort net for genuinely unhandled
exceptions and predates the Result pattern (it maps `Application/Common/Exceptions/*` to
ProblemDetails); prefer Results in new handlers.

### Domain model

`Job : AggregateRoot` is the only aggregate. All state changes go through methods that enforce
the status machine (`Draft → Scheduled → InProgress → Completed`, with `Cancelled` from Scheduled
or InProgress; Completed/Cancelled are terminal) and raise domain events. All timestamps are
normalized with `ToUniversalTime()`; handlers pass `TimeProvider.GetUtcNow()` in rather than the
domain reading the clock. Ids are `Guid.CreateVersion7()`, generated in the handler and passed to
`Job.Create` — the database never generates them (`ValueGeneratedNever`).

Note that both the aggregate and the FluentValidation validators enforce invariants; the validator
guards the request shape, the aggregate guards the domain rule.

### Outbox

`InsertOutboxMessagesInterceptor` is an EF `SaveChangesInterceptor` registered on the DbContext.
On save it scans tracked aggregates for `JobCompletedDomainEvent`, converts each to a
`JobCompletedIntegrationEvent`, writes an `OutboxMessage` row in the same transaction, and removes
the domain event from the aggregate. Only `JobCompletedDomainEvent` is currently mapped —
`JobCreatedDomainEvent` and `JobCancelledDomainEvent` are raised but never leave the aggregate.

`OutboxMessageProcessor` claims a batch with `FOR UPDATE SKIP LOCKED` + a lock lease, publishes via
`IOutboxMessagePublisher`, and applies exponential backoff on failure. It is registered in DI but
**nothing invokes it** — there is no hosted service or endpoint driving it. `OutboxMessagePublisher`
is also a no-op stub. Wiring either up is a deliberate change, not a bug fix in passing.

### Persistence conventions

Default schema `jobs`; snake_case column names configured explicitly in
`Persistence/Configurations/*`. `Address` is an owned value object flattened into `address_*`
columns. `JobStatus` is stored as a string. Full-text search uses a stored computed `tsvector`
column (`SearchVector`, shadow property) with a GIN index, queried through
`EF.Functions.WebSearchToTsQuery`.

Search paging is **keyset (cursor) based**, not offset: the cursor is a base64url-encoded
`(ScheduledDateUtc, JobId)` tuple, ordering by `ScheduledDateUtc ?? 9999-12-31` then `Id`, fetching
`pageSize + 1` rows to detect a next page. Page size is clamped to 1–100 in the repository.

## Known rough edges

Do not "fix" these silently — flag them, since they are load-bearing context:

- `dotnet test` discovers no tests: `JobTracker.Tests` references only `xunit.v3.extensibility.core`
  (no test SDK/runner) and has **no ProjectReference to the src projects**. `JobTests` is a dummy
  assertion. Adding a real test requires fixing the test project first.
- `Program.cs` configures a rate limiter but never calls `app.UseRateLimiter()`, so it is inert.
  The `PermitLimit` is 2/minute — enabling it will break normal usage until tuned.
- `Serilog.AspNetCore` is referenced and documented in the README but not wired into the host.
- `RetryBehaviour` exists but is not registered; only `UnhandledExceptionBehaviour` is in the
  MediatR pipeline. FluentValidation validators are registered but **no validation behaviour runs
  them**, so validators are currently dead code.
- Two identical `PagedList<T>` records exist (`Common/Results` and `Jobs/Queries`); handlers use the
  `Common/Results` one.
- `IDbContext` in `Application/Common/Context` is unimplemented and unused.
- `appsettings.json` contains the dev connection string with a plaintext password (matching
  `docker-compose.yaml`); the `UserSecretsId` is set on the Api project for real secrets.

## API surface

All endpoints are versioned via the `x-api-version` header (default `1.0`, assumed when absent).
Routes come from `[Route("api/[controller]")]` on `ApiControllerBase`, so `JobsController` serves
`/api/jobs`. `GET /` is the health check. `JobTracker.postman_collection.json` at the repo root has
worked examples of each request body.
