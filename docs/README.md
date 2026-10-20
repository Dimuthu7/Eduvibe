# EduVibe docs

- [Requirements and data model](https://claude.ai/code/artifact/3f14c910-0255-48f7-a440-db1953ba76f8) — what EduVibe does, roles, data model, architecture, deployment cost.
- [Sprint plan](https://claude.ai/code/artifact/47646ddb-8f73-4205-8ae4-af233e3682a9) — 10 MVP sprints and 4 Phase 2 sprints, with tickets and status.

- [Backups](backups.md) — the nightly encrypted database backup and how to restore it.
- [Free test deployment](deploy-free.md) — web on Azure, API on Render, database on Neon.
- [Google Drive setup](drive-setup.md) — the Google Cloud client and settings behind class materials.
- [Privacy requests](privacy.md) — downloading and deleting a teacher's or a student's data.
- [Performance check](performance.md) — phone timings and how to repeat them.
- [Launch checklist](launch.md) — what to confirm before real teachers use the app.

## Ticket ids

Tickets are named `S<sprint>-<number>` (for example `S1-2`) and point at requirement ids (for example `ID-1`) in the requirements doc. Use them in branch names, commits and PR titles:

```
s1-2-one-time-password
S1-2: force a new password on first login
```

## Statuses

To do, In progress, Completed. The sprint plan holds the master list; GitHub issues from the `Sprint ticket` template can mirror it on a GitHub Projects board.
