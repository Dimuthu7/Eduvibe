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

Try a restore into a scratch database once before the first real teacher depends on the data.

## Other safety nets

- Neon keeps only the last 6 hours of history on its free plan, so very recent mistakes can be undone from the Neon console (Restore). Anything older needs these backups.
- Migrations that remove or rename a column are done in two releases once real users exist.
