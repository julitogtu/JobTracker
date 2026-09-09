# JobTracker.App

Next.js 16 (App Router, React 19) client for the JobTracker .NET API.

See the [root README](../../README.md) for the whole system — architecture, flow diagrams, and
`docker compose up -d --build` to run everything at once (the UI is then on port 3001).

## Running

The UI reads and writes through the .NET API, so start that first:

```bash
docker compose up -d postgres                          # just the database
dotnet run --project src/JobTracker.Api                # http://localhost:5238

cd src/JobTracker.App
npm install
npm run dev                                            # http://localhost:3000/jobs
```

Configuration lives in `.env.local` (copy of `.env.example`):

| Variable | Purpose |
|---|---|
| `JOBTRACKER_API_URL` | API base URL (default `http://localhost:5238`) |
| `JOBTRACKER_API_VERSION` | Sent as `x-api-version` (default `1.0`) |
| `JOBTRACKER_ORGANIZATION_ID` | Tenant scope for every read and write |

None are `NEXT_PUBLIC_*`: every API call happens on the server (Server Components and Server
Actions), so the API needs no CORS policy and the base URL never reaches the browser bundle.

Scripts: `npm run dev`, `npm run build`, `npm start`, `npm run typecheck`.

## Connecting to the API over HTTPS

`dotnet run --project src/JobTracker.Api` uses the **https** profile by default, binding both
`https://localhost:7163` and `http://localhost:5238`. Because both ports are bound,
`app.UseHttpsRedirection()` knows the HTTPS port and 307-redirects the HTTP one, so pointing
`JOBTRACKER_API_URL` at port 5238 does not avoid TLS -- the redirect lands on 7163 anyway.

That matters because the API presents the **ASP.NET Core development certificate**. It is trusted
by the OS (`dotnet dev-certs https --trust`), but Node ships its own bundled CA list and ignores
the OS store, so a server-side `fetch` fails with:

```
TypeError: fetch failed
  [cause]: Error: self-signed certificate { code: 'DEPTH_ZERO_SELF_SIGNED_CERT' }
```

`npm run dev` / `build` / `start` go through `scripts/with-system-ca.mjs`, which re-spawns the
Next CLI with `NODE_OPTIONS=--use-system-ca` so Node consults the OS certificate store. Nothing
else is required, and TLS verification stays on. (`NODE_OPTIONS` has to be set before Node starts,
which is why it cannot live in `.env.local`.)

Requires Node >= 22.15 for `--use-system-ca`; the wrapper warns and continues on older versions.
Alternatives if you need one:

- Run the API HTTP-only: `dotnet run --project src/JobTracker.Api --launch-profile http`, and set
  `JOBTRACKER_API_URL=http://localhost:5238`. With only the HTTP port bound, the redirect is
  skipped (the API logs "Failed to determine the https port for redirect").
- Export the cert and point Node at it:
  `dotnet dev-certs https --export-path ./devcert.pem --format PEM --no-password`, then set
  `NODE_EXTRA_CA_CERTS=./devcert.pem`.

Do **not** use `NODE_TLS_REJECT_UNAUTHORIZED=0` -- it disables certificate validation process-wide,
including for any other host the app talks to.

`infrastructure/api/http-client.ts` maps these transport failures to a readable message, so a
future occurrence surfaces as actionable text on the error page rather than a bare `fetch failed`.

## Site chrome

`app/layout.tsx` wraps every route in a flex column: `SiteHeader`, the page, then `SiteFooter`
pinned to the bottom on short pages. Both are Server Components with no client JS.

The footer carries the author's contact details -- edit the two constants at the top of
`presentation/components/organisms/site-footer.component.tsx`:

```ts
const TWITTER_HANDLE = 'julitogtu';
const EMAIL = 'julio.avellaneda@outlook.com';
```

Theming is token-based (`src/app/globals.css`): a light palette on `:root` and a dark override
under `@media (prefers-color-scheme: dark)`. Both were checked visually; nothing hard-codes a
colour outside the token block.

## Layer layout

```
src/
├── domain/           entities + repository port (no dependencies)
├── application/      use cases + Server Actions (depend on the port only)
├── infrastructure/   HTTP client, API repository, DI container (server-only)
└── presentation/     stores, shared components, Feature Sliced views
```

`src/infrastructure/di/container.ts` is the composition root and imports `server-only`, so
importing it from a Client Component is a build error rather than a runtime leak.

## The /jobs route

- `app/jobs/(list)/page.tsx` — Server Component. Imports `server-only`, fetches the first page
  through `getContainer().listJobs` (a use case, **not** a Server Action) and passes it to the
  client view as props. The awaited fetch sits in a child component so `<Suspense>` can show
  `JobListSkeleton` while it resolves.
- `app/jobs/(list)/loading.tsx` — route-level skeleton.
- `app/jobs/error.tsx` — route error boundary with a Retry button (`reset()`).
- `app/jobs/not-found.tsx` — custom 404, rendered by `notFound()` in `app/jobs/[jobId]/page.tsx`.

### Why `(list)` is a route group

`loading.tsx` creates an implicit Suspense boundary around its segment **and every child route**.
With `loading.tsx` directly at `app/jobs/`, the shell for `/jobs/[jobId]` flushed with a `200`
before `notFound()` resolved, so invalid job URLs returned **HTTP 200** with 404 content. Putting
the list page and its `loading.tsx` in the `(list)` route group scopes that boundary to `/jobs`
alone, so `/jobs/<bad-id>` returns a real `404`. Route groups do not affect the URL — `/jobs` is
still served by `(list)/page.tsx`.

Verified:

| URL | Status |
|---|---|
| `/jobs` | 200 |
| `/jobs/<valid guid>` | 200 |
| `/jobs/not-a-guid` | 404 |
| `/jobs/<unknown guid>` | 404 |

## Feature Sliced view

```
presentation/views/jobs/
├── components/organisms/jobs-client.component.tsx   'use client', thin shell
├── features/
│   ├── create-job/    hooks/use-create-job.hook.ts   + create-job-modal
│   ├── filter-jobs/   hooks/use-filter-jobs.hook.ts  + job-filter-bar
│   └── complete-job/  hooks/use-complete-job.hook.ts + complete-job-modal
├── hooks/use-jobs-page.hook.ts                      orchestrates the slices
└── index.ts                                         public API
```

Organisms hold no state and define no handlers — every value arrives from a hook.

Server Actions (`application/actions/job.actions.ts`) are used **only** for the two mutations.
`organizationId` is read from server config inside the action and never accepted from the client,
because it is the only tenant boundary the API has: no authentication is configured.

## State

`presentation/stores/use-jobs.store.ts` manages `jobs`, `selectedJobIds`, `filters`,
`pagination`, `sortConfig`. `filteredJobs` is produced by `selectFilteredJobs`, a pure selector —
never `useEffect` + `setState`. Status changes are optimistic: `beginStatusChange` returns a token,
and the hook commits it or calls `rollbackStatusChange` on failure.

### Why a store factory, not a singleton

The store is created per mount by `JobsStoreProvider` rather than at module scope:

1. Zustand v5 serves `getInitialState()` as the `useSyncExternalStore` **server** snapshot, so a
   module-level store mutated during render is invisible to SSR — the list server-rendered as
   "No jobs match" and only filled in after hydration. Seeding the factory fixes that.
2. A module-level store on the server is shared by every concurrent request, which would leak one
   tenant's jobs into another tenant's response.

`jobs` is a hydrated working copy, not a second source of truth: it is seeded from server props,
never fetched on the client, and `revalidatePath('/jobs')` re-seeds it after every mutation.

## Patterns

| Pattern | Where |
|---|---|
| Controlled Component | `job-row.component.tsx` (`isSelected` + `onSelectedChange`), every form input |
| Compound Component | `FilterBar` / `FilterBar.Search` / `.Status` / `.DateRange` / `.Reset` |
| `useReducer` | `use-create-job.hook.ts` — 11 related form fields |
| `useMemo` | derived totals and validation in `use-filter-jobs` / `use-create-job` |
| Error boundary | `ErrorBoundary` wraps `JobList` in `jobs-client.component.tsx` |
| Ternary, never `&&` | all conditional rendering |

## API notes

`JobStatus` is serialised as a **number**, not a string — no `JsonStringEnumConverter` is
registered in `Program.cs` — and the enum is 1-based: `Draft = 1`, `Scheduled = 2`,
`InProgress = 3`, `Completed = 4`, `Cancelled = 5`. `domain/entities/job.entity.ts` mirrors that.

Listing uses keyset (cursor) paging, not offset: the response carries `nextCursor`, which is fed
back as the `Cursor` query parameter.
