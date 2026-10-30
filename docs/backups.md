# Backups

The database is the only thing that cannot be rebuilt, so it is copied every night.

## What runs

`.github/workflows/backup.yml` runs at 02:00 Sri Lanka time (and on demand from the Actions tab). It dumps the database with `pg_dump`, encrypts the dump with a passphrase, and keeps the result as a workflow artifact for 14 days. Nothing readable is stored.

It does nothing until both repository secrets exist (Settings > Secrets and variables > Actions):

| Secret | Value |
| --- | --- |
| `NEON_DATABASE_URL` | The Neon connection string, the direct one (pooling off), the same as the API uses. |
| `BACKUP_PASSPHRASE` | A long random phrase. Keep a copy in a password manager: a backup nobody can decrypt is worthless. |

Run the workflow once by hand after adding them and check it finishes green and uploads `eduvibe-backup`.

The dump image (`postgres:18-alpine`) must be the same Postgres version as Neon or newer. If the Neon project is upgraded, raise it in the workflow too.

## Restore

1. Download the artifact from the workflow run and unzip it.
2. Decrypt: `gpg --output eduvibe.dump --decrypt eduvibe-YYYY-MM-DD.dump.gpg` (it asks for the passphrase).
3. Make an empty database and load it with `pg_restore` from Postgres 18 or newer, as older tools cannot read the dump. A new Neon branch is the safest target; the local docker compose database is Postgres 17, so a restore there may hit version differences: `pg_restore --no-owner --dbname "<connection string>" eduvibe.dump`.
4. Point the API at it (`ConnectionStrings__GuruBook`) and check `/health/ready`.

## Practice restore (do this once)

A backup nobody has restored is only a hope. This rehearsal loads last night's copy into a throwaway database, so the real one is never touched. It needs Docker and about 15 minutes.

1. In the GitHub **Actions** tab open **Back up database**, open the latest green run and download `eduvibe-backup` (a zip) from the bottom of the page. Unzip it into an empty folder.
2. In that folder, decrypt it (it asks for `BACKUP_PASSPHRASE`; if this fails with a bad-passphrase message, the saved passphrase is wrong, which is exactly what this rehearsal is for):
   `gpg --output eduvibe.dump --decrypt eduvibe-YYYY-MM-DD.dump.gpg`
3. In the Neon console open project **eduvibe**, branch **production**, **Databases**, **New database**. Name it `restore_test`. Copy its connection string (direct, pooling off). Check that it ends in `/restore_test?...`; if it says `/neondb`, stop, because that is the real database.
4. Load the dump into it with the Postgres 18 tool, from the same folder:
   `docker run --rm -v "$PWD":/b -e PGURL="<restore_test connection string>" postgres:18-alpine sh -c 'pg_restore --no-owner --dbname "$PGURL" /b/eduvibe.dump'`
   Messages about roles or `owner` can be ignored. A line starting `pg_restore: error:` needs a look.
5. Check the data arrived. In the Neon **SQL Editor**, choose database `restore_test` and run:
   `select (select count(*) from identity.users) as users, (select count(*) from students.students) as students, (select count(*) from fees.payments) as payments;`
   The numbers should match the live app (on 2026-10-30: 5 users, 3 students, 2 payments).
6. Delete the `restore_test` database in the Neon console, and delete `eduvibe.dump` and the decrypted files from your computer, because they hold real data.

Repeat this after any big change to how the backup works, and before the first real teacher depends on the data.

## Other safety nets

- Neon keeps only the last 6 hours of history on its free plan, so very recent mistakes can be undone from the Neon console (Restore). Anything older needs these backups.
- Migrations that remove or rename a column are done in two releases once real users exist.
