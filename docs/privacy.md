# Privacy requests: export and delete

A teacher or a student can ask for the data EduVibe holds about them, or for it to be deleted. Only the Super Admin can do either. Every call needs a Super Admin sign-in.

## A teacher

In the app: **Teachers**, the menu on the teacher's row.

- **Download data** saves a JSON file with the teacher's account, profile, classes, venues, students, enrolments, attendance, fees, exams, notifications, dashboard layout and plan history. Passwords, sign-in tokens and invite tokens are never included.
- **Delete data** appears only for a teacher who has been switched off (**Deactivate** first). It asks for the teacher's full name. Then it deletes the teacher, everything they own, and the students nobody else teaches (with their sign-in accounts).

Students who also study with another teacher stay, with that other teacher's records.

## A student

Students are shared between teachers, so this is done by API only, as it should be rare. With a Super Admin token and the student's id (from the teacher's export, or the `studentId` in any enrolment):

```
GET  /api/privacy/students/{id}/export
POST /api/privacy/students/{id}/erase     { "confirm": "<student's full name>" }
```

Erasing a student removes who they are (name, phone, school, guardian, sign-in account) and keeps their enrolments, so every teacher's fee, attendance and marks totals stay correct. The records read "Deleted student". Their notifications are deleted with the account.

## Notifications

In-app notifications are addressed to one person (a teacher or a student account). A teacher's are included in their export and deleted with them. Read ones are removed after 90 days and any after 180 days. They hold names only inside the short text parameters of the notification (for example a class title or an amount).

## What the audit log keeps

The audit log is a trail of who changed what. When data is erased, its rows stay (who, when, which kind of record) but the before and after values, which hold names and numbers, are replaced with `{"erased":true}`. One new row records that the erasure was done and by whom.

## How it works, and what can go wrong

- Each module that holds personal data registers an `IPersonalDataSource` (`src/Shared/EduVibe.Shared/Privacy`). The Privacy module calls them in order and has no tables of its own. **A new module that stores personal data must register one**, usually a one-line subclass of `TenantTablesSource`.
- Erasing runs module by module without one database transaction. If it stops part way (for example the database goes away), run the same request again: every step is safe to repeat. A teacher who is half erased still shows up in the export until the last step finishes.
- Erasing cannot be undone. Download the data first, and note that backups (see [backups](backups.md)) keep older copies until they expire.
