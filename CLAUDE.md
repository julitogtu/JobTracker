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

# Tests (xunit v3 on Microsoft.Testing.Platform -- opted in via global.json)
dotnet test --solution JobTracker.slnx
dotnet test --project tests/JobTracker.Tests/JobTracker.Tests.csproj
dotnet test --project tests/JobTracker.Tests/JobTracker.Tests.csproj -- --filter-class "*JobTests"

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

### Tests

`JobTracker.Tests` references all four src projects. Domain unit tests live in
`tests/JobTracker.Tests/Domain/`, one file per type under test (`JobTests`, `JobPhotoTests`,
`AddressTests`, `EntityTests`, `ValueObjectTests`). `JobFactory` builds a `Job` in each status
from timestamps derived from a single `CreatedAt` constant, so the aggregate's ordering rules hold
by construction and a test only states the one it deliberately breaks. The aggregate is exercised
through its public API only -- `JobPhoto`'s constructor is `internal`, so photo cases go through
`Job.AddPhoto`.

Integration tests live in `tests/JobTracker.Tests/Integration/` and need **Docker running**:
`PostgresFixture` starts one `postgres:17-alpine` Testcontainer for the whole assembly and
applies the migrations once. They exercise the real composition root -- `AddApplication()` +
`AddPersistence()`, the same two calls `Program.cs` makes -- with only the clock substituted
(`MutableTimeProvider`, which works because handlers take `TimeProvider` by injection).

Tests share one database and isolate by tenant: each test instance gets a fresh
`OrganizationId`, and since every repository query is tenant-scoped, no truncation between tests
is needed. Outbox rows are the exception -- the table has no organization column, so outbox
assertions match on the job id inside the jsonb payload. A real database is not optional here:
the stored `tsvector` column, the GIN index and the row-value keyset comparison have no
in-memory equivalent.

The suite covers four seams: `JobPersistenceTests` (EF mapping fidelity, asserted against real
columns), `JobSearchTests` (full-text, filters, keyset paging), `OutboxTests` (the SaveChanges
interceptor, including that the row and the job update share one transaction), and
`JobPipelineTests` (`Mediator.Send` end to end, including tenant isolation and the exact
`ErrorType` each handler returns). The HTTP layer is deliberately **not** covered -- that needs
`WebApplicationFactory`, which needs `Program` made public.

### End-to-end tests

```powershell
docker compose up -d                      # required: the suite resets its tenant via psql
cd src/JobTracker.App
npm run test:e2e                          # browser -> Next -> .NET API -> PostgreSQL
npm run test:e2e -- --grep completing   # one describe block
npm run test:e2e:ui                       # Playwright UI mode
```

Playwright specs live in `src/JobTracker.App/e2e/`. **Run them through `npm run test:e2e`, not
`npx playwright test`** -- `scripts/run-e2e.mjs` mints a fresh `JOBTRACKER_ORGANIZATION_ID` and
passes it to the Next server it starts, which is what gives the run a private slice of the shared
database. The config starts both servers itself (the API on 5238 via the `http` profile, Next on
**3100** so an existing `npm run dev` on 3000 is untouched).

Two constraints worth knowing before changing the setup:

- Tests run **serially with one worker**, and an auto-fixture empties the tenant before each test
  with `docker exec jobtracker-postgres psql`. The organization id is per *run*, not per test --
  it is server environment and the Next server starts once -- so without that reset any assertion
  about a total is really an assertion about test order. It goes to SQL because the API has no
  delete endpoint.
- Specs seed state through the **API**, not the UI, and assert through the browser. That is not
  just for speed: the React app has no start-job feature, so `InProgress` is unreachable from the
  UI and the complete-job flow could not be set up any other way.

`playwright.config.ts` pins `channel: 'chrome'` (the installed browser) rather than Playwright's
bundled Chromium, and disables video: `npx playwright install` times out on this network, and a
missing ffmpeg binary fails the test rather than degrading. Traces and screenshots are retained on
failure.

## Playwright MCP

`.mcp.json` registers the Playwright MCP server for interactive browser driving. It is pinned to
`--browser chrome` for the same reason as above -- the bundled Chromium is not downloadable here.
Claude Code loads `.mcp.json` at startup, so it needs a restart (and approval of the new server)
before the tools appear. This is separate from the e2e suite: the MCP is for exploring the running
app by hand, the suite is what runs in CI.

## Known rough edges

Do not "fix" these silently — flag them, since they are load-bearing context:

- `dotnet test` needs an explicit target. The .NET 10 SDK dropped VSTest, so the repo opts into
  Microsoft.Testing.Platform in `global.json`; the bare `dotnet test` form then reports
  "Zero tests ran" instead of failing loudly. Pass `--project` or `--solution`, and put filters
  after `--` as MTP options (`-- --filter-class "*JobTests"`) -- the old
  `--filter "FullyQualifiedName~..."` syntax is gone.
- FluentAssertions 8.10 prints an Xceed license warning on every test run: it is free for
  non-commercial use only, and commercial use needs a paid subscription. Worth deciding on
  deliberately before the suite grows.
- `Program.cs` configures a rate limiter but never calls `app.UseRateLimiter()`, so it is inert.
  The `PermitLimit` is 2/minute — enabling it will break normal usage until tuned.
- MediatR 14 is licensed **RPL-1.5 or commercial** (Lucky Penny), not Apache-2.0 like 12.x.
  Unlicensed, every start logs "You do not have a valid license key for MediatR ... you are
  required to have a licensed version" -- allowed for development, not for production. Set
  `MEDIATR_LICENSE_KEY` (or `cfg.LicenseKey` in `AddApplication`) when that matters. The last
  Apache-2.0 release is 12.5.0 and it uses the same API this codebase calls.
- `JobTracker.Application` declares `Microsoft.Extensions.Logging.Abstractions` explicitly.
  It used to arrive only as a transitive dependency of MediatR, so `ILogger<T>` in the
  pipeline behaviours would stop compiling if MediatR ever dropped it.
- FluentValidation validators are registered but **no validation behaviour runs them**, so
  validators are currently dead code.
- Two identical `PagedList<T>` records exist (`Common/Results` and `Jobs/Queries`); handlers use the
  `Common/Results` one.
- `IDbContext` in `Application/Common/Context` is unimplemented and unused.
- `appsettings.json` contains the dev connection string with a plaintext password (matching
  `docker-compose.yaml`); the `UserSecretsId` is set on the Api project for real secrets.

## Retry

`RetryBehaviour` is a MediatR pipeline behaviour backed by a Polly v8 resilience pipeline,
registered in `AddApplication` as `AddResiliencePipeline(ResiliencePipelines.RequestRetry)` and
resolved through `ResiliencePipelineProvider<string>`.

**3 retries at 1s, 2s and 4s** (exponential, base 1s, `MaxDelay` 4s), so a failing request makes 4
attempts over ~7 seconds before giving up. Jitter is **off** so the delays are exactly those
values; turn `UseJitter` back on if many callers ever retry in lockstep.

**Every exception is retried except cancellation.** `ResiliencePipelines.ShouldRetry` walks the
`InnerException` chain and refuses only when it finds an `OperationCanceledException` — cancelling
is control flow, not a failure to ride out. Everything else, including bugs like a
`NullReferenceException`, costs 4 attempts and ~7 seconds before surfacing.

**Retry is opt-in, and commands deliberately do not opt in.** A request is retried only if it
implements `IRetryableRequest`; `GetJobByIdQuery` and `SearchJobsQuery` do, the three commands do
not. That is not caution for its own sake — retrying a command in this pipeline is *wrong*, and
would silently corrupt behaviour:

- The DbContext is scoped and its change tracker survives a failed `SaveChangesAsync`. A retried
  `CompleteJobCommandHandler` re-runs `GetByIdAsync`, which returns the **already-mutated tracked
  instance**. Its status is now `Completed`, so the handler's own `job.Status != InProgress` guard
  fails and the retry returns a 409 for a job it just completed.
- A save that committed but lost the connection before acknowledging would be re-applied.

Making a command retryable means making its handler idempotent first — a fresh scope per attempt,
or a guard keyed on the operation. Adding the marker to a command without that is a bug, not a
configuration change.

The pipeline's `TimeProvider` comes from DI (`GetService<TimeProvider>() ?? TimeProvider.System`),
which is what keeps `RetryBehaviourTests` fast: it registers a provider whose timers fire
immediately, so the tests assert the real 1s/2s/4s schedule without waiting for it.

Pipeline order is `UnhandledExceptionBehaviour` then `RetryBehaviour`, so exhausted retries are
logged once as an error by the outer behaviour rather than once per attempt. `RetryBehaviourTests`
pins that order.

## Correlation ids

`CorrelationIdMiddleware` (registered first in the pipeline) gives every request an id, taken from
the inbound `X-Correlation-Id` header when the caller sent one and minted with
`Guid.CreateVersion7()` when they did not. It is echoed on the response, pushed to the Serilog
`LogContext` alongside the W3C `TraceId`, and attached to every ProblemDetails body as
`correlationId` -- so a caller reporting a failure can quote an id that finds the request.

The inbound header is **validated, not trusted**: at most 128 characters from
`[A-Za-z0-9-_.:]`, anything else discarded and a fresh id minted. It goes into every log line for
the request, so an unvalidated value lets a caller forge log entries with newlines and bloat log
storage at will.

Reach it from:

- **Api** -- `HttpContext.GetCorrelationId()`.
- **Application / Persistence** -- `ICorrelationIdAccessor` (`Application/Common/Correlation`),
  implemented by `HttpCorrelationIdAccessor`. It returns `ICorrelationIdAccessor.None` when there
  is no request in scope, so a hosted service can depend on it safely.

Serilog is now wired into the host (`builder.Logging.ClearProviders()` +
`AddSerilog(... Enrich.FromLogContext())`, configured from the `Serilog` section of
`appsettings.json`, plus `UseSerilogRequestLogging()`). **`Enrich.FromLogContext()` is
load-bearing**: without it `LogContext.PushProperty` silently enriches nothing and the middleware
looks like it works -- header still returned, logs still written, correlation id nowhere.
`CorrelationIdMiddlewareTests` asserts on real log events specifically to catch that.

### The outbox carries it too

`OutboxMessage` has a `correlation_id` column (indexed, migration `AddOutboxCorrelationId`).
`InsertOutboxMessagesInterceptor` takes `ICorrelationIdAccessor` and stamps the id of the request
that produced the domain event, in the same transaction as the job update. `OutboxEnvelope` then
carries it to `IOutboxMessagePublisher`, so a downstream consumer receives it. That is the link
that survives the request ending: a row drained minutes later still names the request that caused
it.

`AddPersistence` registers `NullCorrelationIdAccessor` with `TryAddScoped`, so Persistence stands
alone in tests and background hosts. The Api registers `HttpCorrelationIdAccessor` **before**
`AddPersistence` in `Program.cs`, which is what makes the `TryAdd` a no-op there -- reordering
those two lines silently reverts every outbox row to `none`.

Still missing for a full chain: **outbound propagation**. When this service starts calling others,
an outgoing HttpClient needs a `DelegatingHandler` that forwards `X-Correlation-Id`, and the
publisher (still a no-op stub) needs to put it on the wire as a message header.

## API surface

All endpoints are versioned via the `x-api-version` header (default `1.0`, assumed when absent).
Routes come from `[Route("api/[controller]")]` on `ApiControllerBase`, so `JobsController` serves
`/api/jobs`. `GET /` is the health check. `JobTracker.postman_collection.json` at the repo root has
worked examples of each request body.
