# JobTracker

[![CI](https://github.com/julitogtu/JobTracker/actions/workflows/ci.yml/badge.svg)](https://github.com/julitogtu/JobTracker/actions/workflows/ci.yml)

A multi-tenant field-service job tracker: a .NET 10 API, a Next.js 16 client, and a Hangfire
worker that turns a completed job into an invoice and a customer email — without ever losing one
to a crash, thanks to the transactional outbox.

It is a demo, but a deliberately realistic one. The interesting parts are not the CRUD; they are
the seams: Clean Architecture with an enforced dependency direction, `Result` instead of
exceptions for expected failures, keyset pagination over PostgreSQL full-text search, an outbox
drained out of process, and a correlation id that survives all of it.

---

## Table of contents

- [Main features](#main-features)
- [Technologies](#technologies)
- [Architecture](#architecture)
- [Getting started](#getting-started)
- [Main flows](#main-flows)
- [Correlation ids](#correlation-ids)
- [API surface](#api-surface)
- [Testing](#testing)
- [Continuous integration](#continuous-integration)
- [Project structure](#project-structure)
- [Known rough edges](#known-rough-edges)
- [Design analysis: denormalize, join, or sync?](#design-analysis-denormalize-join-or-sync)
- [Events, delivery, and idempotency](#events-delivery-and-idempotency)

---

## Main features

| Feature | What it means here |
|---|---|
| **Multi-tenant by construction** | Every query is scoped by `OrganizationId`. It is not a filter you can forget — it is a parameter on the repository. |
| **Enforced job lifecycle** | `Draft → Scheduled → InProgress → Completed`, with `Cancelled` reachable from two states. The aggregate rejects every other transition. |
| **Result pattern, not exceptions** | Expected failures return `Result<T>` with a typed `Error`; `ErrorType` maps to the HTTP status. Exceptions are for bugs. |
| **Transactional outbox** | A completed job writes its integration event in the *same transaction* as the status change. No dual-write, no lost event. |
| **Async fan-out via Hangfire** | A background host drains the outbox and dispatches to two independent modules — Billing and Notifications — on separate queues. |
| **Full-text search + keyset paging** | A stored `tsvector` column with a GIN index, paged by an opaque cursor rather than `OFFSET`. |
| **Correlation ids end to end** | One id ties an HTTP request, its logs, its ProblemDetails body, and the background work it triggered minutes later. |
| **Retry policy that knows what is safe** | Polly retries queries; commands deliberately opt out, because retrying a non-idempotent handler in a shared `DbContext` corrupts behaviour. |
| **Server-rendered UI** | Next.js App Router. Every API call happens on the server, so the API needs no CORS policy and the base URL never reaches the browser. |
| **Tested against real infrastructure** | Integration tests run against a real PostgreSQL in Testcontainers; e2e drives a real browser through the whole stack. |

---

## Technologies

**Backend**

| | |
|---|---|
| .NET 10 / C# 14 | Target framework `net10.0` |
| MediatR | CQRS dispatch + pipeline behaviours |
| FluentValidation | Request-shape validation |
| EF Core 10 + Npgsql | Persistence, migrations, interceptors |
| PostgreSQL 17 | `tsvector` search, `FOR UPDATE SKIP LOCKED`, `jsonb` |
| Hangfire 1.8 | Background scheduling, storage in its own `hangfire` schema |
| Polly v8 | Retry resilience pipeline |
| Serilog | Structured logging with `LogContext` enrichment |
| Asp.Versioning | Versioning via the `x-api-version` header |
| Scalar | API reference UI (Development only) |
| SendGrid | Customer email delivery |

**Frontend**

| | |
|---|---|
| Next.js 16 (App Router) | Server Components + Server Actions |
| React 19 | |
| Zustand | Client-side view state |
| TypeScript 5.9 | |

**Testing & tooling**

| | |
|---|---|
| xUnit v3 on Microsoft.Testing.Platform | Unit + integration suites |
| Testcontainers | A real PostgreSQL per test run |
| FluentAssertions | Assertions |
| Playwright | Browser end-to-end suite |
| Docker Compose | The whole stack in one command |
| GitHub Actions | CI |

---

## Architecture

Clean Architecture, with the dependency arrows pointing inward. `Domain` knows about nothing;
`Application` knows about `Domain`; everything else depends on those two.

```mermaid
flowchart TB
    BROWSER["Browser"]

    subgraph app["JobTracker.App — Next.js 16"]
        NEXTSRV["Server Components<br/>+ Server Actions"]
    end

    subgraph api["JobTracker.Api — ASP.NET Core"]
        MW["CorrelationIdMiddleware"]
        CTRL["JobsController"]
    end

    subgraph application["JobTracker.Application"]
        BEHAV["Pipeline behaviours<br/>UnhandledException then Retry"]
        HANDLERS["Command and query handlers<br/>internal sealed"]
        PORTS["Ports: IUnitOfWork<br/>IOutboxMessagePublisher<br/>ICorrelationIdAccessor"]
    end

    subgraph domain["JobTracker.Domain"]
        AGG["Job : AggregateRoot<br/>status machine, domain events"]
        REPO["IJobRepository"]
    end

    subgraph contracts["JobTracker.Jobs.IntegrationEvents"]
        IE["JobCompletedIntegrationEvent<br/>no project references"]
    end

    subgraph persistence["JobTracker.Persistence"]
        EF["JobsDbContext, JobRepository"]
        INT["InsertOutboxMessagesInterceptor"]
        PROC["OutboxMessageProcessor"]
    end

    subgraph worker["JobTracker.BackgroundJob — Hangfire"]
        DISPATCH["OutboxDispatchJob<br/>recurring, every minute"]
        FANOUT["HangfireOutboxDispatcher"]
        BILLING["Billing module"]
        NOTIF["Notifications module"]
    end

    DB[("PostgreSQL 17<br/>schemas: jobs, hangfire")]
    SENDGRID["SendGrid"]

    BROWSER --> NEXTSRV
    NEXTSRV -->|"HTTP + x-api-version"| MW
    MW --> CTRL
    CTRL -->|"Mediator.Send"| BEHAV
    BEHAV --> HANDLERS
    HANDLERS --> AGG
    HANDLERS --> REPO
    HANDLERS --> PORTS
    REPO -.implemented by.-> EF
    PORTS -.implemented by.-> EF
    EF --> INT
    EF --> DB
    INT --> IE
    DISPATCH --> PROC
    PROC --> DB
    PROC --> FANOUT
    FANOUT --> IE
    FANOUT --> BILLING
    FANOUT --> NOTIF
    NOTIF --> SENDGRID
```

**Two projects sit outside the layered stack.**
`JobTracker.Jobs.IntegrationEvents` is the published contract — what leaves the Jobs boundary — and
has **no project references at all**, so a consumer can depend on the shape of an event without
pulling in the aggregate, EF or MediatR. `JobTracker.BackgroundJob` is the async host; it is the
only process that knows Billing and Notifications exist.

**The Api references Persistence for exactly one line** — `AddPersistence(connectionString)` at
startup. It never touches an EF type.

---

## Getting started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — required; the integration and e2e tests need it too
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — only if you want to run the hosts outside containers
- [Node.js 24+](https://nodejs.org) — only if you want to run the UI outside containers

### Option A — the whole stack in one command

```bash
git clone https://github.com/julitogtu/JobTracker.git
cd JobTracker

docker compose up -d --build
```

That builds and starts five services:

| Service | URL | Notes |
|---|---|---|
| `postgres` | `localhost:5432` | container name `jobtracker-postgres` |
| `migrator` | — | applies the EF migrations, then exits |
| `api` | http://localhost:5240 | Scalar reference at `/scalar` |
| `backgroundjob` | http://localhost:5241 | Hangfire dashboard at `/hangfire` |
| `app` | http://localhost:3001 | the UI |

Open **http://localhost:3001** and you have a working system.

> **The `migrator` service is not optional.** Neither host applies migrations on boot, so without
> it the `jobs` schema would not exist and every request would fail. `api` and `backgroundjob`
> both wait on it completing successfully.

Stop it, and reclaim the database volume:

```bash
docker compose down          # stop
docker compose down -v       # stop and delete the data
```

### Option B — run the services from source

The compose ports (5240 / 5241 / 3001) deliberately avoid the launch-profile ports
(5238 / 5239 / 3000), so **you can run the whole stack and a local host at the same time** — which
is the normal case when you are working on one service and want the other three just running.

**1. Start PostgreSQL**

```bash
docker compose up -d postgres
```

**2. Apply the migrations**

`JobsDbContextFactory` is the design-time context and reads `JOBTRACKER_CONNECTION_STRING`, which
is why Persistence acts as its own startup project:

```powershell
# PowerShell
$env:JOBTRACKER_CONNECTION_STRING="Host=localhost;Port=5432;Database=jobtracker;Username=jobtracker;Password=jobtracker_dev_password;SSL Mode=Disable"
dotnet ef database update --project src/JobTracker.Persistence --startup-project src/JobTracker.Persistence
```

```bash
# bash
export JOBTRACKER_CONNECTION_STRING="Host=localhost;Port=5432;Database=jobtracker;Username=jobtracker;Password=jobtracker_dev_password;SSL Mode=Disable"
dotnet ef database update --project src/JobTracker.Persistence --startup-project src/JobTracker.Persistence
```

**3. Run the API** — http://localhost:5238, Scalar at `/scalar`

```bash
dotnet run --project src/JobTracker.Api --launch-profile http
```

**4. Run the background host** — http://localhost:5239, dashboard at `/hangfire`

```bash
dotnet run --project src/JobTracker.BackgroundJob --launch-profile http
```

**5. Run the UI** — http://localhost:3000/jobs

```bash
cd src/JobTracker.App
npm install
npm run dev
```

### Try it end to end

Create a job, walk it through the lifecycle, and watch the async side pick it up:

```bash
ORG=8f6c1b52-1f3d-4a2e-9c77-2b1d5f0e4a10
API=http://localhost:5240        # or 5238 if you are running from source

# 1. Create it — scheduled, so it starts in Scheduled rather than Draft
JOB=$(curl -s -X POST $API/api/jobs \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: my-first-job" \
  -d '{"title":"Fix HVAC","description":"Replace the failed compressor.",
       "street":"123 Main St","city":"Austin","state":"TX","zipCode":"78701",
       "latitude":30.2672,"longitude":-97.7431,
       "customerId":"11111111-2222-4333-8444-555555555555","organizationId":"'$ORG'",
       "scheduledDateUtc":"2030-01-01T09:00:00Z","assigneeId":"99999999-8888-4777-8666-555555555555"}' \
  | sed -E 's/.*"id":"([^"]+)".*/\1/')

# 2. Start it, then complete it
curl -s -X POST $API/api/jobs/start -H "Content-Type: application/json" \
  -H "X-Correlation-Id: my-first-job" -d '{"organizationId":"'$ORG'","jobId":"'$JOB'"}'

curl -s -X POST $API/api/jobs/complete -H "Content-Type: application/json" \
  -H "X-Correlation-Id: my-first-job" \
  -d '{"organizationId":"'$ORG'","jobId":"'$JOB'","signatureUrl":"https://example.com/sig.png"}'
```

Within a minute the background host drains the outbox. Its log shows the invoice and the email —
both tagged with `my-first-job`:

```
[12:32:07 INF] [my-first-job] Job 01a08626-... completed: queued invoice generation and customer notification.
[12:32:07 INF] [my-first-job] Billing: raising invoice for job 01a08626-... Idempotency key 01a08626...
[12:32:07 INF] [my-first-job] No SendGrid API key configured, so this mail was not sent. To: dev-inbox@jobtracker.local ...
```

```bash
docker compose logs -f backgroundjob
```

`JobTracker.postman_collection.json` at the repo root has worked examples of every request body.

---

## Main flows

### 1. Job lifecycle (the state machine)

The aggregate is the only thing that can move a job between states, and it refuses every
transition not drawn here. `Completed` and `Cancelled` are terminal.

```mermaid
stateDiagram-v2
    [*] --> Draft: Job.Create
    Draft --> Scheduled: Schedule(date, assignee)
    Scheduled --> InProgress: Start()
    InProgress --> Completed: Complete(signatureUrl)
    Scheduled --> Cancelled: Cancel(reason)
    InProgress --> Cancelled: Cancel(reason)
    Completed --> [*]
    Cancelled --> [*]

    note right of Scheduled
        the date must be in the future
    end note
    note right of InProgress
        requires an assignee
    end note
    note right of Completed
        raises JobCompletedDomainEvent
        the only event that leaves the aggregate
    end note
```

`Job.Create` can jump straight to `Scheduled` if given a date and an assignee together — supplying
one without the other is rejected.

### 2. Creating a job (synchronous request flow)

Every write follows this path. Note where the two guards sit: the validator checks the *request
shape*, the aggregate checks the *domain rule*. Both, deliberately.

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant MW as CorrelationIdMiddleware
    participant Ctl as JobsController
    participant Beh as Pipeline behaviours
    participant H as CreateJobCommandHandler
    participant Agg as Job (aggregate)
    participant Repo as IJobRepository
    participant Db as PostgreSQL

    C->>MW: POST /api/jobs (X-Correlation-Id?)
    MW->>MW: validate header, or mint Guid.CreateVersion7()
    MW->>Ctl: request + LogContext CorrelationId, TraceId
    Ctl->>Beh: Mediator.Send(CreateJobCommand)
    Note over Beh: UnhandledExceptionBehaviour then RetryBehaviour.<br/>Commands do not implement IRetryableRequest,<br/>so nothing is retried here.
    Beh->>H: Handle(command)
    H->>H: id = Guid.CreateVersion7()<br/>now = TimeProvider.GetUtcNow()
    H->>Agg: Job.Create(id, ..., now)
    Agg-->>H: Job, raising JobCreatedDomainEvent
    H->>Repo: AddAsync(job)
    H->>Db: IUnitOfWork.SaveChangesAsync()
    Db-->>H: committed
    H-->>Ctl: Result.Success(id)
    Ctl-->>C: 201 Created + X-Correlation-Id
```

**On failure** the handler returns `Result.Failure(new Error("Jobs.Create....", ..., ErrorType))`,
and `ApiControllerBase.ToProblem` maps it:

| `ErrorType` | HTTP |
|---|---|
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| anything else | 500 |

The ProblemDetails body carries a `correlationId` field, so a caller reporting a failure can quote
an id that finds the request.

### 3. Completing a job (the asynchronous flow)

This is the flow the whole design exists for. A completed job has to raise an invoice and email
the customer — two things that must not be lost, must not be done twice in a way that matters, and
must not hold the HTTP request open.

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant H as CompleteJobCommandHandler
    participant Agg as Job (aggregate)
    participant Int as InsertOutboxMessagesInterceptor
    participant Db as PostgreSQL
    participant Hf as OutboxDispatchJob (Hangfire)
    participant Proc as OutboxMessageProcessor
    participant Disp as HangfireOutboxDispatcher
    participant B as Billing module
    participant N as Notifications module

    rect rgb(238, 244, 252)
    Note over C,Db: One HTTP request, one database transaction
    C->>H: POST /api/jobs/complete
    H->>Agg: job.Complete(now, signatureUrl)
    Agg-->>H: raises JobCompletedDomainEvent
    H->>Db: SaveChangesAsync()
    Db->>Int: SavingChanges
    Int->>Int: map to JobCompletedIntegrationEvent,<br/>stamp the correlation id
    Int->>Db: INSERT jobs.outbox_messages (same transaction)
    Int->>Agg: RemoveDomainEvent
    Db-->>H: COMMIT, job row and outbox row together
    H-->>C: 204 No Content
    end

    rect rgb(245, 240, 250)
    Note over Hf,N: Another process, up to a minute later
    Hf->>Proc: ExecuteAsync(batchSize)
    Proc->>Db: SELECT ... FOR UPDATE SKIP LOCKED + lock lease
    Db-->>Proc: claimed batch
    Proc->>Disp: PublishAsync(OutboxEnvelope)
    Disp->>Disp: route on type, deserialize
    Disp->>B: Enqueue InvoiceGenerationJob on queue "billing"
    Disp->>N: Enqueue CustomerNotificationJob on queue "notifications"
    Disp-->>Proc: done
    Proc->>Db: UPDATE processed_on_utc
    end

    B->>B: IInvoiceService.GenerateAsync
    N->>N: resolve customer, then IEmailSender (SendGrid)
```

**Why an outbox at all.** The alternative is writing the job update to PostgreSQL and publishing
the event to a broker — two systems, no shared transaction. Crash between them and you have either
a completed job nobody billed for, or an invoice for a job that was never completed. The outbox
turns that into a single commit; delivery becomes a separate, retryable problem.

**Failure handling at each stage:**

```mermaid
flowchart LR
    A["Outbox row<br/>pending"] -->|dispatcher throws| B["MarkFailed:<br/>retry_count++,<br/>exponential backoff,<br/>reason in last_error"]
    B --> A
    A -->|enqueued OK| C["MarkProcessed"]
    C --> D["Hangfire owns it now"]
    D -->|module throws| E["Hangfire retry,<br/>10 attempts"]
    E --> D
    E -->|exhausted| F["Failed state:<br/>visible in the dashboard,<br/>requeueable by a human"]
```

An unrecognised event type or an unreadable payload **throws** rather than being skipped. Marking
such a row processed would make the backlog disappear along with the invoice.

> **Delivery is at-least-once, twice over.** If the second `Enqueue` throws, the row is never
> marked processed and the next drain enqueues both again; and Hangfire itself can run a job
> twice. Both module commands therefore carry the event's `IdempotencyKey` — and **the two module
> implementations are stubs that do not yet dedupe on it.** That is the first thing to add when
> either starts doing real work.

### 4. Searching jobs (full-text + keyset paging)

Paging is cursor-based, not `OFFSET`-based: an opaque base64url cursor encodes the
`(ScheduledDateUtc, JobId)` of the last row, and the next page is a row-value comparison against
it. Stable under concurrent inserts, and it does not get slower deeper into the results.

```mermaid
flowchart TB
    Q["GET /api/jobs?organizationId=...&searchTerm=hvac&pageSize=25"] --> H["SearchJobsQueryHandler<br/>IRetryableRequest, so this one IS retried"]
    H --> R["JobRepository.SearchAsync"]
    R --> T["WHERE organization_id = @org"]
    T --> FT["AND search_vector @@ websearch_to_tsquery(@term)<br/>stored tsvector column, GIN index"]
    FT --> F["AND status / scheduled range / assignee filters"]
    F --> K["AND (coalesce(scheduled_date_utc,'9999-12-31'), id) > (@cursorDate, @cursorId)"]
    K --> O["ORDER BY coalesce(scheduled_date_utc,'9999-12-31'), id<br/>LIMIT pageSize + 1"]
    O --> P["The extra row proves a next page exists"]
    P --> RESP["PagedList of JobResponse + next cursor"]
```

`pageSize` is clamped to 1–100 in the repository, so a client cannot ask for the whole table.

---

## Correlation ids

A single request can touch the browser, the Next server, the API, PostgreSQL, and — minutes later,
in a different process — Hangfire, Billing and SendGrid. When something goes wrong, "which request
caused this?" has to be answerable from a log line.

```mermaid
flowchart TB
    IN["Client sends X-Correlation-Id: checkout-svc-7f3a91<br/>or omits it entirely"]
    VAL{"Valid?<br/>at most 128 chars,<br/>only A-Z a-z 0-9 - _ . :"}
    MINT["Mint Guid.CreateVersion7()"]
    CTX["LogContext.PushProperty(CorrelationId, TraceId)"]

    IN --> VAL
    VAL -->|no| MINT --> CTX
    VAL -->|yes| CTX

    CTX --> R1["Response header<br/>X-Correlation-Id"]
    CTX --> R2["Every Serilog line<br/>for this request"]
    CTX --> R3["ProblemDetails body<br/>correlationId field"]
    CTX --> ACC["ICorrelationIdAccessor<br/>read by Application and Persistence"]

    ACC --> ROW["outbox_messages.correlation_id<br/>written in the same transaction"]

    ROW -.->|"the request has long since ended"| ENV["OutboxEnvelope.CorrelationId"]
    ENV --> AMB["AmbientCorrelationIdAccessor<br/>AsyncLocal, background host"]
    AMB --> CMD["GenerateInvoiceCommand.CorrelationId<br/>NotifyCustomerOfCompletionCommand.CorrelationId"]
    CMD --> LOGS["Billing and Notifications log lines,<br/>minutes later, in another process"]
```

**The inbound header is validated, not trusted.** It goes into every log line for the request, so
an unvalidated value would let a caller forge log entries with newlines and inflate log storage at
will. Anything over 128 characters or outside `[A-Za-z0-9-_.:]` is discarded and a fresh id minted.

**How to reach it:**

| From | Use |
|---|---|
| Api | `HttpContext.GetCorrelationId()` |
| Application / Persistence | `ICorrelationIdAccessor` — returns `"none"` when no request is in scope, so a hosted service can depend on it safely |
| Background host | `AmbientCorrelationIdAccessor`, restored from the outbox row |

**Two registration-order traps**, both silent if you get them wrong:

- `Enrich.FromLogContext()` is load-bearing. Without it `LogContext.PushProperty` enriches
  nothing — the header is still returned and the logs are still written, so it looks like it
  works, but the id is nowhere.
- `AddPersistence` registers `NullCorrelationIdAccessor` with `TryAddScoped`. Both hosts register
  their real accessor **before** calling it. Swap those two lines and every outbox row silently
  reverts to `none`.

**What it buys you.** A row drained minutes after the request ended still names the request that
caused it:

```
[12:31:37 INF] [checkout-svc-7f3a91] HTTP POST /api/jobs/complete responded 204
...
[12:32:07 INF] [checkout-svc-7f3a91] Job 01a08626-... completed: queued invoice generation and customer notification.
[12:32:07 INF] [checkout-svc-7f3a91] Billing: raising invoice for job 01a08626-...
[12:32:07 INF] [checkout-svc-7f3a91] Notifying customer 11111111-... that job 01a08626-... is complete.
```

Grep one id and you have the whole causal chain, across two processes and a minute of wall time.

**Still missing:** outbound propagation. When this service starts calling others, an outgoing
`HttpClient` needs a `DelegatingHandler` that forwards the header, and the outbox publisher needs
to put it on the wire as a message header.

---

## API surface

All endpoints are versioned via the `x-api-version` header (default `1.0`, assumed when absent).

| Method | Route | Description |
|---|---|---|
| `GET` | `/` | Health check |
| `GET` | `/api/jobs/{organizationId}/{jobId}` | Fetch one job |
| `GET` | `/api/jobs` | Search — `organizationId`, `searchTerm`, `statuses`, `scheduledFromUtc`, `scheduledToUtc`, `assigneeId`, `cursor`, `pageSize` |
| `POST` | `/api/jobs` | Create a job (optionally scheduled) |
| `POST` | `/api/jobs/start` | `Scheduled` to `InProgress` |
| `POST` | `/api/jobs/complete` | `InProgress` to `Completed`, raises the integration event |

Error codes follow `Jobs.<Operation>.<Reason>` — for example `Jobs.Start.InvalidStatus`.

---

## Testing

```bash
# Everything. --solution is required: global.json opts into Microsoft.Testing.Platform,
# and a bare `dotnet test` reports "Zero tests ran" instead of failing.
dotnet test --solution JobTracker.slnx

# One class
dotnet test --project tests/JobTracker.Tests/JobTracker.Tests.csproj -- --filter-class "*JobTests"
```

| Suite | Location | Needs |
|---|---|---|
| Domain unit tests | `tests/.../Domain/` | nothing |
| Application unit tests | `tests/.../Application/` | nothing |
| Background-host unit tests | `tests/.../BackgroundJob/` | nothing |
| Integration tests | `tests/.../Integration/` | **Docker** — one `postgres:17-alpine` Testcontainer for the run |
| End-to-end | `src/JobTracker.App/e2e/` | **Docker** + Chrome |

Integration tests share one database and isolate by **tenant**: each test instance gets a fresh
`OrganizationId`, and since every repository query is tenant-scoped, no truncation is needed
between tests. Outbox rows are the exception — that table has no organization column — so outbox
assertions match on the job id inside the `jsonb` payload.

### End-to-end

```bash
docker compose up -d postgres     # the suite resets its tenant via docker exec ... psql
cd src/JobTracker.App
npm run test:e2e                  # browser -> Next -> .NET API -> PostgreSQL
npm run test:e2e -- --grep completing
npm run test:e2e:ui               # Playwright UI mode
```

Run them through `npm run test:e2e`, **not** `npx playwright test`: the wrapper mints a fresh
`JOBTRACKER_ORGANIZATION_ID` per run, which is what gives the run a private slice of the shared
database. The config starts both servers itself (API on 5238, Next on **3100** so an existing
`npm run dev` on 3000 is untouched).

---

## Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main`, on every pull request, and on demand.

```mermaid
flowchart LR
    T["push / pull_request"] --> B["backend<br/>build + test, Release"]
    T --> W["web<br/>typecheck + build"]
    T --> E["e2e<br/>Playwright, real stack"]
    T --> I["images<br/>docker compose build"]
```

Four jobs in parallel. Two notes on why they are shaped this way:

- **Release, not Debug.** Test ordering differs between configurations, and the integration suite
  shares one outbox table with no tenant column — so an assertion that quietly assumes it only
  sees its own rows passes in one configuration and fails in the other.
- **`images` earns its slot.** The Dockerfiles carry hand-written restore lists, so adding a
  project to the solution can leave them copying a tree that no longer compiles — something
  `dotnet build` will never notice.

---

## Project structure

```
JobTracker/
├── .github/workflows/ci.yml
├── docker-compose.yaml                    # postgres, migrator, api, backgroundjob, app
├── Directory.Packages.props               # central NuGet versions; csproj files carry no Version
├── JobTracker.slnx
├── src/
│   ├── JobTracker.Domain/                 # aggregate, value objects, domain events, repository port
│   ├── JobTracker.Application/            # CQRS handlers, Result/Error, behaviours, ports
│   ├── JobTracker.Jobs.IntegrationEvents/ # published contracts, no project references
│   ├── JobTracker.Persistence/            # EF Core, configurations, outbox, migrations
│   ├── JobTracker.Api/                    # controllers, middleware, filters, Program.cs
│   ├── JobTracker.BackgroundJob/          # Hangfire host
│   │   ├── Outbox/                        #   drain and dispatch
│   │   ├── Billing/                       #   bounded context: invoice generation
│   │   └── Notifications/                 #   customer email via SendGrid
│   └── JobTracker.App/                    # Next.js 16 client
└── tests/
    └── JobTracker.Tests/                  # unit, integration and background-host suites
```

---

## Known rough edges

These are deliberate and load-bearing — documented rather than silently "fixed":

- **`dotnet test` needs an explicit target.** The .NET 10 SDK dropped VSTest; the repo opts into
  Microsoft.Testing.Platform in `global.json`, and the bare form reports "Zero tests ran" instead
  of failing. Filters go after `--` (`-- --filter-class "*JobTests"`).
- **Both async modules are stubs.** `InvoiceService` logs the invoice it would raise, and neither
  module dedupes on `IdempotencyKey` yet.
- **The outbox drain is a minutely cron**, so a completed job waits up to a minute.
  `OutboxDispatch:CronExpression` is the knob; Hangfire's floor is one minute.
- **The Hangfire dashboard is Development-only** and authorized by a filter that allows everyone.
  Hangfire's default allows local requests only, which 401s from outside a container. Serving it
  anywhere but a developer machine needs a real filter.
- **The rate limiter is configured but never used** — `Program.cs` never calls
  `app.UseRateLimiter()`, and `PermitLimit` is 2/minute, so enabling it will break normal usage
  until tuned.
- **FluentValidation validators are registered but nothing runs them** — there is no validation
  pipeline behaviour, so they are currently dead code.
- **MediatR 14 is RPL-1.5 or commercial**, not Apache-2.0 like 12.x. Unlicensed use logs a warning
  on every start; allowed for development, not for production. Set `MEDIATR_LICENSE_KEY` when that
  matters.
- **FluentAssertions 8.10 is free for non-commercial use only** and prints an Xceed licence
  warning on every test run.
- **`appsettings.json` contains the dev connection string with a plaintext password**, matching
  `docker-compose.yaml`. A `UserSecretsId` is set on the Api project for real secrets.

`CLAUDE.md` carries the deeper design notes — retry semantics, persistence conventions, and the
reasoning behind each of the above.

---

## Design analysis: denormalize, join, or sync?

`Job` stores a bare `CustomerId` — nothing about the customer lives on the row. Rendering a job
list with customer names leaves three options.

**Join from Contacts at read time.** Correct by construction: there is one copy, so it cannot be
stale. This is right when the value must be authoritative at the instant it is read — the billing
address on an invoice, a credit hold — or when it changes often enough that any copy would usually
be wrong. The cost is coupling and latency: a cross-context join binds the two schemas together
and blocks the read on both. It would also break the keyset query, which pages over `jobs` alone.

**Denormalize a frozen snapshot.** Copy the name onto the job at creation and never touch it.
Right when you want the *historical* value — the customer as they were when the work was done,
which is what a signed job sheet or an audit trail means. Such a copy is not stale; it is
deliberately point-in-time, and needs no synchronization.

**Denormalize and sync with integration events.** Keep the local copy, and let Contacts publish
`CustomerRenamed` so Jobs updates it. Right when you want current values without the coupling: the
read stays local and fast, and freshness becomes a background concern, not a query problem.

**The trade-offs.** The join is strongly consistent and structurally coupled — one schema change
away from breaking two contexts. The snapshot is perfectly consistent with a *different* question,
and wrong only if you expected "current". The synced replica is eventually consistent: there is a
window in which Jobs shows the old name, and you inherit the entire delivery problem — ordering,
retries, duplicates, and eventually a reconciliation job for the events that go missing anyway.

---

## Events, delivery, and idempotency

### Domain events within a module, integration events across

`JobCompletedDomainEvent` and `JobCompletedIntegrationEvent` carry almost the same fields. They
are separate types on purpose, and the difference is not ceremony — it is who is allowed to break
them.

A **domain event** is an internal fact. `JobCompletedDomainEvent` lives in `JobTracker.Domain`,
is raised by the aggregate inside `Job.Complete`, and is consumed in the same process, in the same
transaction, by `InsertOutboxMessagesInterceptor`. Nothing outside the solution ever sees it, so it
can hold domain types, and renaming a field is a compiler problem — you find every caller and fix
them in one commit. Its job is to let the aggregate say *what happened* without knowing who cares.

An **integration event** is a published contract. `JobCompletedIntegrationEvent` lives in
`JobTracker.Jobs.IntegrationEvents`, a project with **no project references at all**, because a
consumer must be able to depend on the shape of the event without dragging in the aggregate, EF or
MediatR. It is serialized to `jsonb`, read back by a different process minutes later, and one day
by a service you do not deploy. Renaming a field there is a breaking change you cannot fix in one
commit — which is exactly why it is a different type, in a different assembly, with a deliberately
narrow shape.

`InsertOutboxMessagesInterceptor` is the single translation point between the two vocabularies. It
also calls `RemoveDomainEvent` after mapping, so a second `SaveChanges` on the same tracked
aggregate cannot write the row twice.

Note what is *not* translated: `JobCreatedDomainEvent` and `JobCancelledDomainEvent` are raised and
then dropped on save. Only completion is a fact other contexts have asked for, so only completion
becomes a contract. Raising a domain event costs nothing; publishing an integration event commits
you to supporting it.

---

### Why the outbox gives at-least-once delivery

The guarantee comes from two halves, and it is worth being precise about which half does what.

**Nothing is ever lost, because the message is written by the same transaction as the fact.**
`InsertOutboxMessagesInterceptor` runs inside `SaveChanges`, so the `jobs.jobs` update and the
`jobs.outbox_messages` insert are one commit. There is no window in which a job is Completed but
no message exists, and none in which a message exists for a job that was never completed. Either
both landed or neither did. `OutboxTests.OutboxMessage_AndTheJobUpdate_ShareOneTransaction` pins
this by rolling back and asserting that the row went with it.

Without the outbox you have a dual write — commit to PostgreSQL, then publish to a broker — with
no transaction spanning both. Crash in between and you have a completed job nobody billed for.

**Nothing is delivered at most once, because publishing and recording that fact cannot share a
transaction.** `OutboxMessageProcessor` claims a batch (`FOR UPDATE SKIP LOCKED` plus a lock
lease), publishes, then writes `processed_on_utc` in a *separate* write against a *different*
system. Die between those two and the row is still pending, so the next drain publishes it again.
The lock lease adds a second duplicate source: a worker that dies holding a claim has that claim
expire, and another worker picks the row up.

That is a deliberate choice, not a gap. The two orderings available are:

| Order | Failure mode | Guarantee |
|---|---|---|
| publish, then mark processed | crash after publish → redelivered | **at-least-once** |
| mark processed, then publish | crash after mark → message lost | at-most-once |

Exactly-once delivery is not on the menu — it would need a distributed transaction across
PostgreSQL and the broker. What *is* achievable is exactly-once **effect**, and that is bought on
the consumer side, with idempotency.

---

### Idempotency in the invoice handler

Because delivery is at-least-once, `InvoiceGenerationJob` can run twice for one completed job —
from a redelivered outbox row, or from Hangfire retrying the job itself. Raising two invoices for
one job is exactly the kind of duplicate that costs money, so the contract carries the key that
makes the second run a no-op:

```csharp
// JobTracker.Jobs.IntegrationEvents/JobCompletedIntegrationEvent.cs
public string IdempotencyKey => $"{JobId:N}:{CompletedAtUtc:O}";
```

**Why `JobId + CompletedAtUtc`, and not `EventId`.** `EventId` identifies a *message*;
`IdempotencyKey` identifies a *business fact*. They coincide for a plain redelivery — the same row
carries the same `EventId` — but they diverge everywhere else. `EventId` is a fresh
`Guid.NewGuid()` minted inside `Job.Complete`, so a replayed backfill, a migration that re-emits
history, or a second producer describing the same completion would each mint a new one and slip
past a dedupe keyed on it. `JobId + CompletedAtUtc` is derived entirely from the fact itself, so
any two messages describing the same completion produce the same key, no matter who produced them.

**Why the timestamp is in the key rather than `JobId` alone.** Today `Completed` is terminal and
`CompletedAtUtc` is written exactly once, so the key is stable for the life of the job — `JobId`
alone would be sufficient. It is not future-proof, though. If the lifecycle ever gains a
reopen-and-re-complete transition, the second completion is a genuinely new fact that *deserves* a
second invoice, and a key of `JobId` alone would silently suppress it. Including the timestamp
means the key changes exactly when the fact changes.

**How a real handler would enforce it.** The key travels into `GenerateInvoiceCommand`, so
`InvoiceService` has everything it needs:

```csharp
// Billing owns the guard, because Billing owns the invoice.
if (await invoices.ExistsAsync(command.IdempotencyKey, cancellationToken))
    return;

await invoices.AddAsync(Invoice.For(command), cancellationToken);
```

The check alone is not enough — two concurrent deliveries can both pass it. The guard has to be a
**unique constraint** on the key column, with the insert catching the violation and returning
successfully. The database settles the race; the `Exists` check is only there to avoid the
exception on the common path.

> ⚠️ **This is designed for, not yet enforced.** `InvoiceService` and `CustomerNotificationJob` are
> stubs: they receive `IdempotencyKey` and log it, and neither stores anything to dedupe against.
> A duplicate delivery today produces a duplicate log line, which is harmless only because nothing
> real happens yet. Adding the store above is the first thing either module needs before it does
> real work — see [Known rough edges](#known-rough-edges).

The same reasoning applies to the notification: sending one customer two "your job is done" emails
is less costly than two invoices, but it is still a defect, and the key is already on
`NotifyCustomerOfCompletionCommand` for it.
