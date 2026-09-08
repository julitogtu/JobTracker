---
name: new-feature
description: Scaffold a new frontend verb slice (use case, repository method, Server Action, hook, modal, barrel) in the JobTracker Next.js app, following the repo's layered + atomic-design conventions. Use when adding a job operation to the UI - e.g. "add a start-job button", "wire up cancel job in the app", "new reschedule feature in JobTracker.App".
---

# New Frontend Verb Slice

Scaffolds the slice that `create-job`, `complete-job`, and `filter-jobs` all follow in
`src/JobTracker.App`. This is the frontend counterpart to `new-slice` (which scaffolds the C#
CQRS slice behind it). Apply the templates in `templates/` rather than writing these files from
memory — they encode the optimistic-update protocol and the client/server boundary, both of
which fail silently when they are wrong.

**This skill is the UI half only.** If the API endpoint does not exist yet, run `new-slice` first
and come back — every template here assumes there is already a route to call.

## Step 1 — Settle the parameters

Fill this table before writing anything. If the user didn't say, infer from the aggregate method
in `src/JobTracker.Domain/Jobs/Job.cs` and the corresponding controller action, and state your
assumption.

| Placeholder | Meaning | Example |
|---|---|---|
| `__PASCAL__` | Feature name, PascalCase, verb + Job | `StartJob` |
| `__KEBAB__` | Folder and file name | `start-job` |
| `__CAMEL__` | Use case key on the container, action prefix | `startJob` |
| `__REPO_METHOD__` | Method on `JobRepository` | `start` |
| `__TARGET_STATUS__` | `JobStatus` member the row flips to | `InProgress` |
| `__GUARD__` | Guard in `job.entity.ts` | `canStart` |
| `__LABEL__` | Button and modal title | `Start job` |
| `__PENDING_LABEL__` | In-flight button text | `Starting...` |
| `__STATUS_MESSAGE__` | Warning shown when the guard fails | `Only a scheduled job can be started.` |
| `__USE_CASE_RESULT__` | `void` for transitions, `string` for create | `void` |
| `__ACTION_DATA__` | `undefined`, or `{ id: string }` for create | `undefined` |
| `__EXTRA_ARGS__` | Extra payload fields passed to the use case | `, signatureUrl: payload.signatureUrl` |
| `__VALIDATOR__` | C# validator the hook's checks mirror | `StartJobCommandValidator` |

**Check the repository port first.** `JobRepository` in `src/domain/repositories/job.repository.ts`
already declares `search`, `getById`, `create`, `complete`, and `start` — and `start` is
**implemented in `JobApiRepository` but unused above the port**, so a start-job feature needs no
infrastructure work at all. If the method is missing, add the `__PASCAL__Input` interface and the
port method there, then implement it in `JobApiRepository` against the real route (check
`JobTracker.postman_collection.json` for the body shape).

**Check the guard exists.** `job.entity.ts` has `canComplete` only. A new feature almost always
needs a new guard next to it, mirroring the C# status machine:

```ts
/** Mirrors the domain status machine so the UI never offers an impossible transition. */
export function canStart(status: JobStatus): boolean {
  return status === JobStatus.Scheduled;
}
```

## Step 2 — Write the files

Read each template, substitute every placeholder, write to:

| Template | Destination |
|---|---|
| `use-case.ts.template` | `src/application/use-cases/__KEBAB__.use-case.ts` |
| `action.ts.template` | append to `src/application/actions/job.actions.ts` |
| `hook.ts.template` | `src/presentation/views/jobs/features/__KEBAB__/hooks/use-__KEBAB__.hook.ts` |
| `modal.component.tsx.template` | `src/presentation/views/jobs/features/__KEBAB__/components/organisms/__KEBAB__-modal.component.tsx` |

Paths are relative to `src/JobTracker.App`. The action template is a snippet, not a file — add the
function to `job.actions.ts` alongside `createJobAction` and `completeJobAction`; it reuses that
file's existing `toErrorMessage` helper and imports.

Then write the barrel by hand at `features/__KEBAB__/index.ts` — three lines, matching
`features/complete-job/index.ts`:

```ts
export { use__PASCAL__ } from './hooks/use-__KEBAB__.hook';
export type { Use__PASCAL__Result } from './hooks/use-__KEBAB__.hook';
export { __PASCAL__Modal } from './components/organisms/__KEBAB__-modal.component';
```

### Three edits the templates cannot make for you

1. **`action-result.ts`** — add the `__PASCAL__Payload` interface. It must live here, not in
   `job.actions.ts`: a `'use server'` module may only export async functions, so a type exported
   from it is a build error.
2. **`container.ts`** — add the use case to the `Container` interface and to `build()`. There is no
   assembly scan here; unlike the C# side, an unregistered use case simply does not exist.
3. **`use-jobs-page.hook.ts` and `jobs-client.component.tsx`** — call the hook in `useJobsPage`,
   expose it on `UseJobsPageResult`, and render `<__PASCAL__Modal controller={...} />` in
   `JobsView`. Row-level triggers go through an `on__PASCAL__Requested` callback passed down to
   `JobList`, the way `onCompleteRequested` already does.

## Conventions these templates encode

- **The container is server-only.** `container.ts` and `http-client.ts` open with
  `import 'server-only'`. Importing either from a Client Component is a build error rather than a
  runtime leak of `JOBTRACKER_API_URL` into the browser bundle. Client code reaches the API
  *only* through a Server Action.
- **`organizationId()` is read from server config and never accepted from the client.** It is the
  only tenant boundary in the system — the API has no authentication behind it. A payload
  interface that carries `organizationId` is a bug, not a convenience.
- **Server Actions are mutations only.** Reads are fetched in the Server Component
  (`app/jobs/(list)/page.tsx`) through the container. Never add a read action.
- **Actions return `ActionResult<T>`, never throw.** `ApiError` is caught and flattened to
  `{ ok: false, error }` by `toErrorMessage`; the hook renders `result.error` directly.
- **Every mutation ends with `revalidatePath('/jobs')`** so the server re-sends a fresh page.
  `useJobsPage` re-hydrates the store only when the `initialPage` prop is a *different object*
  (see its `lastPage` ref) — skip the revalidate and the list silently goes stale.
- **Components are thin shells.** All state and handlers live in the hook; the modal destructures
  a single `controller` prop and defines no `useState`. Keep it that way.
- **The layers point inward**: `presentation → application → domain`, with `infrastructure`
  reachable only through `container.ts`. A hook importing `job-api.repository` is a layering
  violation the compiler will not catch.
- **Status is a number, not a string.** The API serialises `JobStatus` as a 1-based number (no
  `JsonStringEnumConverter` is registered in `Program.cs`). Always compare against the `JobStatus`
  const object, never a string literal.

## Step 3 — Verify

```bash
cd src/JobTracker.App && npm run typecheck
```

The `Stop` hook in `.claude/settings.json` runs `dotnet build` only — nothing typechecks the app
automatically, so run this yourself before reporting the feature done.

There is **no linter, formatter, or test runner** in this project: no ESLint, no Prettier, no
Vitest. `npm run typecheck` is the only automated check that exists. Match the surrounding style
by hand — 2-space indent, single quotes, semicolons, trailing commas, explicit return types on
exported functions.

To exercise it end to end the API must be running (`dotnet run --project src/JobTracker.Api`), and
use `npm run dev` rather than `next dev` — the wrapper in `scripts/with-system-ca.mjs` sets
`--use-system-ca` so Node trusts the ASP.NET Core dev certificate.

## When the operation is not a status transition

The templates assume a status change, because the store's optimistic API
(`beginStatusChange` / `commitStatusChange` / `rollbackStatusChange` in `use-jobs.store.ts`) only
snapshots and restores `status`. For an operation that changes something else — reschedule,
reassign, add a photo — there is no optimistic path in the store. Either extend it with a matching
snapshot action, or drop the optimistic block from the hook and rely on `revalidatePath` alone.
Say which one you chose; do not quietly leave a `beginStatusChange` call that snapshots the wrong
field.

## When the operation needs a form

`complete-job` (one field, `useState`) and `create-job` (eleven fields, `useReducer`) are the two
worked examples. Past roughly three fields use `useReducer` with a `{ type: 'field' }` /
`{ type: 'reset' }` action pair and derive `errors` in a `useMemo` — `create-job` shows the whole
shape.

Client-side validation mirrors the C# validator for fast feedback. Be aware that on the API side
those FluentValidation validators are **dead code** — no pipeline behaviour runs them — so the
aggregate is the real enforcement, and this UI check is the only one the user sees before the
request goes out. Do not describe it as merely duplicating a server check.
