# EduVibe

App for private tuition teachers in Sri Lanka: classes, students, fees, attendance and exam marks. Minimal, fast on phones, easy to extend.

## Layout

- `src/Api/EduVibe.Api` — the ASP.NET Core host. Only wires modules together; `ModuleCatalog.cs` lists them.
- `src/Modules/<Name>/EduVibe.Modules.<Name>` — one project per feature area (Identity, Classes, Students, Fees, Attendance, Exams, Plans). Each owns its PostgreSQL schema, entities, endpoints and migrations.
- `src/Shared/EduVibe.Shared` — tenancy, auditing, base entity, module interface and persistence wiring.
- `web/` — Angular PWA (standalone components, signals, Angular Material).
- `tests/` — xUnit tests. `web/**/*.spec.ts` — Vitest unit tests.

## Rules

- Modules never reference each other's projects or tables. Talk through an interface in Shared or an event.
- Teacher-owned entities implement `ITenantOwned`; do not write tenant filters by hand. Changes to fees, attendance, marks, enrolment and accounts implement `IAuditable`.
- New feature = new module project, added to `ModuleCatalog`.
- All UI text goes in `web/public/i18n/*.json` and is shown with the `t` pipe. Never hard-code text in templates.
- Web UI uses Angular Material only, with theme tokens (`var(--mat-sys-*)`) so light and dark both work. Screens use the shared pieces: `page` classes, `app-submit-button`, `Loadable` for lists, `ToastService` for success and failure messages, `inlineError` for errors shown beside a form. The HTTP error interceptor and `GlobalErrorHandler` are the safety net; do not add try/catch toasts around them.
- Money is `decimal` in LKR. Times are Asia/Colombo. Fee months are year-month.
- Plans decide what a teacher can use. Gate new paid features on a plan feature key.

## Commands

- API: `dotnet build EduVibe.slnx` · `dotnet test EduVibe.slnx` · `dotnet run --project src/Api/EduVibe.Api`
- Database: `docker compose up -d`; migrations apply on start in Development.
- New migration for a module: `dotnet dotnet-ef migrations add <Name> -p <module project> -s src/Api/EduVibe.Api -c <Module>DbContext -o Migrations`
- Web: `cd web && npm ci && npm start` · `npm test -- --watch=false` · `npm run build`

## Docs

Requirements, data model, sprint plan and cost estimate live in the project's Claude Docs; see `docs/README.md` for links.
