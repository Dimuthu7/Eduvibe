# EduVibe

A minimal, phone-first app that helps private tuition teachers in Sri Lanka run their classes: students and enrolment, monthly fees, attendance, and exam marks with grades and ranks.

**Stack:** ASP.NET Core (.NET 10) modular monolith · Angular PWA · PostgreSQL · Azure.

## Run it locally

You need the .NET 10 SDK, Node.js 22.12 or newer, and Docker.

```bash
docker compose up -d                      # PostgreSQL on localhost:5432
dotnet run --project src/Api/EduVibe.Api  # API on http://localhost:5077 (migrates the database on start)

cd web
npm ci
npm start                                 # app on http://localhost:4200
```

Open http://localhost:4200/status to confirm the app can reach the API.

## Test

```bash
dotnet test EduVibe.slnx
cd web && npm test -- --watch=false
```

## Repository layout

| Path | What it holds |
| --- | --- |
| `src/Api` | The API host that wires the modules together |
| `src/Modules` | One project per feature: Identity, Classes, Students, Fees, Attendance, Exams, Plans |
| `src/Shared` | Tenancy, audit log, base entity and persistence wiring shared by every module |
| `web` | Angular progressive web app |
| `tests` | API and shared-kernel tests |
| `docs` | Links to the requirements, data model and sprint plan |

See [docs](docs/README.md) for the plan and [CLAUDE.md](CLAUDE.md) for the conventions to follow.

## Configuration

| Setting | Where | Notes |
| --- | --- | --- |
| `ConnectionStrings__EduVibe` | API environment | PostgreSQL connection string |
| `Database__MigrateOnStartup` | API environment | `true` applies migrations on start; on in Development |
| `Cors__AllowedOrigins__0` | API environment | Web app address allowed to call the API |
| `apiBaseUrl` | `web/public/config.json` | Replaced per environment at deploy time |
