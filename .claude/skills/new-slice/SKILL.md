---
name: new-slice
description: Scaffold a new CQRS vertical slice (command record, handler, validator, controller action) in the JobTracker API, following the repo's Result/Error conventions. Use when adding a new job operation or endpoint - e.g. "add a CancelJob command", "new endpoint to reschedule a job", "add an AddPhoto slice".
---

# New CQRS Vertical Slice

Scaffolds the slice that `CreateJob`, `StartJob`, and `CompleteJob` all follow. Apply the templates in
`templates/` rather than writing these files from memory — they encode naming and error codes that
must agree across files.

## Step 1 — Settle the parameters

Fill this table before writing anything. If the user didn't say, infer from the aggregate method in
`src/JobTracker.Domain/Jobs/Job.cs` and state your assumption.

| Placeholder | Meaning | Example |
|---|---|---|
| `__NAME__` | Slice name, PascalCase, verb + Job | `CancelJob` |
| `__OP__` | Error-code segment | `Cancel` |
| `__RESULT__` | `Unit` for state transitions, `Guid` for create | `Unit` |
| `__ROUTE__` | Route suffix, lowercase | `cancel` |
| `__REQUIRED_STATUS__` | `JobStatus` the job must be in | `InProgress` |
| `__PAST_TENSE__` | Verb for the error message | `cancelled` |
| `__AGGREGATE_METHOD__` | Method on `Job` this calls | `Cancel` |
| `__EXTRA_PARAMS__` | Extra command params, or empty | `,` + newline + `    string Reason` |
| `__EXTRA_ARGS__` | Extra args to the aggregate method | `, command.Reason` |

**Check the aggregate method exists first.** `Job` already has `Schedule`, `Start`, `Complete`,
`Cancel`, and `AddPhoto`. If the operation needs a method that isn't there, add it to `Job` in the
same style — enforce the transition, call `EnsureNotTerminal()`, normalize with `ToUniversalTime()`,
take `occurredOnUtc` as a parameter rather than reading a clock, and raise a domain event.

## Step 2 — Write the files

Read each template, substitute every placeholder, write to:

| Template | Destination |
|---|---|
| `Command.cs.template` | `src/JobTracker.Application/Jobs/Commands/__NAME__/__NAME__Command.cs` |
| `CommandHandler.cs.template` | `src/JobTracker.Application/Jobs/Commands/__NAME__/__NAME__CommandHandler.cs` |
| `CommandValidator.cs.template` | `src/JobTracker.Application/Jobs/Commands/__NAME__/__NAME__CommandValidator.cs` |
| `ControllerAction.cs.template` | insert into `src/JobTracker.Api/Controllers/JobsController.cs` |

The controller template is a snippet, not a file — add the action to `JobsController` alongside the
existing ones and add the `using` for the new command namespace.

**No DI registration is needed.** `AddApplication` calls `AddMediatR(RegisterServicesFromAssembly)`
and `AddValidatorsFromAssembly`, so the handler and validator are picked up by assembly scan even
though both are `internal sealed`. Don't go looking for a registry to edit.

## Conventions these templates encode

- **Handlers are `internal sealed`**; only the command record is `public`. Handler and command live in
  separate files (queries vary — `GetJobByIdQuery.cs` holds both, `SearchJobs` splits them).
- **Return `Result<T>`, never throw** for expected failures. `ApiControllerBase.ToProblem` maps
  `ErrorType` to the status code, so the type on the `Error` is what picks 404 vs 409 vs 400.
- **Use the `Error.NotFound(...)` / `Error.Conflict(...)` factories**, not `new Error(...)`.
  `CompleteJobCommandHandler` predates the factories and uses the raw constructor, which silently
  leaves `ErrorType.Failure` and returns **500** where it means 409 — don't copy that file.
- **Error codes are `Jobs.<Op>.<Reason>`** — `NotFound`, `InvalidStatus`, `DomainError`.
- **Load through `IJobRepository.GetByIdAsync(organizationId, jobId, ct)`.** Always pass
  `OrganizationId` — it is the only tenant isolation in the codebase, and there is no authentication
  behind it. A query that omits it leaks across organizations.
- **Time comes from injected `TimeProvider`**, never `DateTime.UtcNow`.
- **The status precheck is duplicated** — the handler checks it to return a clean `Conflict`, and the
  aggregate re-checks it and throws `DomainException`, caught as a fallback. Keep both.

## Step 3 — Verify

```bash
dotnet build --nologo -v q
```

Then check the slice by hand against the list above. Two things not to be misled by:

- **The validator does not run.** Validators are registered but no `IPipelineBehavior` invokes them —
  there is no `ValidationBehaviour` in `AddApplication`. The file is worth writing for when one is
  added, but it enforces nothing today. Every invariant the slice actually depends on must live in the
  handler or the aggregate.
- **`dotnet test` discovers no tests.** `JobTracker.Tests` has no test runner and no `ProjectReference`
  to `src/`, so it exits clean without running anything. Don't report a passing test run as evidence.

## If the slice raises a new domain event

`InsertOutboxMessagesInterceptor` maps **only** `JobCompletedDomainEvent` to an outbox row. A new
domain event will be raised on the aggregate and then silently dropped on save. To publish it, add an
integration event under `Domain/Jobs/IntegrationEvents/` and extend the interceptor. Note the outbox
is not drained either — `OutboxMessageProcessor` is registered but never invoked, and
`OutboxMessagePublisher` is a no-op stub. Say so rather than implying the event will be delivered.
