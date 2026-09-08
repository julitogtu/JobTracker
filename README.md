# JobTracker

A RESTful API for tracking and managing jobs, built with .NET 10 following Clean Architecture principles.

## Architecture

The solution is organized into four projects:

| Project | Description |
|---|---|
| `JobTracker.Api` | ASP.NET Core Web API — controllers, filters, and startup configuration |
| `JobTracker.Application` | Application layer — CQRS commands/queries via MediatR, validation via FluentValidation |
| `JobTracker.Domain` | Domain layer — entities, value objects, and domain logic |
| `JobTracker.Persistence` | Persistence layer — EF Core with PostgreSQL |

## Tech Stack

- **.NET 10** / **C# 14**
- **MediatR** — CQRS pattern
- **FluentValidation** — request validation
- **Entity Framework Core** + **Npgsql** — data access with PostgreSQL
- **Serilog** — structured logging
- **Asp.Versioning** — API versioning via `x-api-version` header
- **Scalar** — API reference UI (development only)
- **Rate Limiting** — fixed window rate limiter (global)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL instance

## Getting Started

1. **Clone the repository**

   ```bash
   git clone <repository-url>
   cd JobTracker
   ```

2. **Configure the database connection**

   Update `appsettings.Development.json` in `src/JobTracker.Api` (or use user secrets):

   ```json
   {
	 "ConnectionStrings": {
	   "JobsDatabase": "Host=localhost;Database=jobtracker;Username=<user>;Password=<password>"
	 }
   }
   ```

3. **Apply database migrations**

   ```bash
   dotnet ef database update --project src/JobTracker.Persistence --startup-project src/JobTracker.Api
   ```

4. **Run the API**

   ```bash
   dotnet run --project src/JobTracker.Api
   ```

   The API will be available at `https://localhost:<port>`.  
   The Scalar API reference UI is available at `https://localhost:<port>/scalar` (development only).

## API Endpoints

All endpoints are versioned. Pass the desired version via the `x-api-version` request header (default: `1.0`).

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/jobs/{organizationId}/{jobId}` | Get a job by ID |
| `POST` | `/api/jobs` | Create a new job |
| `POST` | `/api/jobs/start` | Start a job |
| `POST` | `/api/jobs/complete` | Complete a job |

## Running Tests

```bash
dotnet test
```

## Project Structure

```
JobTracker/
├── src/
│   ├── JobTracker.Api/          # Web API host
│   ├── JobTracker.Application/  # Application logic (CQRS)
│   ├── JobTracker.Domain/       # Domain model
│   └── JobTracker.Persistence/  # EF Core / PostgreSQL
└── tests/
	└── JobTracker.Tests/        # Test project
```
