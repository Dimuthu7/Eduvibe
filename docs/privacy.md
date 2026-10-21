# Privacy requests: export and delete

A teacher or a student can ask for the data EduVibe holds about them, or for it to be deleted. Only the Super Admin can do either. Every call needs a Super Admin sign-in.

## A teacher

In the app: **Teachers**, the menu on the teacher's row.

- **Download data** saves a JSON file with the teacher's account, profile, classes, venues, students, enrolments, attendance, fees, exams, notifications, WhatsApp message log, dashboard layout and plan history, the students' profile photos (as base64 text under `student_photos`), and the class materials list and the connected Google account's email. Passwords, sign-in tokens, invite tokens and the Google Drive token are never included.
- **Delete data** appears only for a teacher who has been switched off (**Deactivate** first). It asks for the teacher's full name. Then it deletes the teacher, everything they own, and the students nobody else teaches (with their sign-in accounts).

Students who also study with another teacher stay, with that other teacher's records.

## A student

Students are shared between teachers, so this is done by API only, as it should be rare. With a Super Admin token and the student's id (from the teacher's export, or the `studentId` in any enrolment):

```
GET  /api/privacy/students/{id}/export
POST /api/privacy/students/{id}/erase     { "confirm": "<student's full name>" }
```

Erasing a student removes who they are (name, phone, school, guardian, profile photo, sign-in account) and keeps their enrolments, so every teacher's fee, attendance and marks totals stay correct. The records read "Deleted student". Their notifications are deleted with the account.

## Profile photos

A student's photo is one small JPEG (about 320 px square, 15 to 40 kB) in `students.student_photos`, shared like the student record: any teacher who teaches the student can see or replace it. It is part of the student's export, is deleted with the student when a teacher's erasure removes students nobody else teaches, and is deleted when the student is erased. It is never shown to other students.

## WhatsApp messages

`whatsapp.messages` is a log of every message a teacher sent: the student's name, the parent's phone number (guardian's number when there is one), the words sent (first name, class, month, amount owed, or marks, grade and rank), the status, and a key that stops repeats. `whatsapp.opt_outs` holds the numbers whose owners replied STOP. Both are tenant-owned, so they are part of the teacher's export and are erased with the teacher. They also hold a student's personal data, so a student's export includes their messages, and erasing a student deletes those rows and any opt-out for numbers that have no other messages left. The teacher's WhatsApp screen shows only the last four digits of a number. Meta, as the carrier, keeps its own records under its terms.

## Class materials

The files live in the teacher's own Google Drive, not in EduVibe. EduVibe holds only a list (`materials.materials`: title, file name, type, size, class, folder, topic, year, and the Drive file id) and the teacher's folders (`materials.material_folders`: name, parent and Drive folder id), the connected Google account's email and the encrypted Drive token (`materials.drive_connections`). The list and email are part of the teacher's export; the token is never exported. Erasing a teacher first tells Google to drop EduVibe's access (best effort), then deletes the list, the folder ids and the connection. Files in the teacher's Drive are theirs and are not touched. Students' data is not stored in this module.

## Notifications

In-app notifications are addressed to one person (a teacher or a student account). A teacher's are included in their export and deleted with them. Read ones are removed after 90 days and any after 180 days. They hold names only inside the short text parameters of the notification (for example a class title or an amount).

## What the audit log keeps

The audit log is a trail of who changed what. When data is erased, its rows stay (who, when, which kind of record) but the before and after values, which hold names and numbers, are replaced with `{"erased":true}`. One new row records that the erasure was done and by whom.

## How it works, and what can go wrong

- Each module that holds personal data registers an `IPersonalDataSource` (`src/Shared/EduVibe.Shared/Privacy`). The Privacy module calls them in order and has no tables of its own. **A new module that stores personal data must register one**, usually a one-line subclass of `TenantTablesSource`.
- Erasing runs module by module without one database transaction. If it stops part way (for example the database goes away), run the same request again: every step is safe to repeat. A teacher who is half erased still shows up in the export until the last step finishes.
- Erasing cannot be undone. Download the data first, and note that backups (see [backups](backups.md)) keep older copies until they expire.
