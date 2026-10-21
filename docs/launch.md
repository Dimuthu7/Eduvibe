# Launch checklist

Tick items off as they are done. Items marked *deferred* wait for the end of the free-hosting period (decision of 9 Oct 2026).

## Before real teachers use it

- [ ] API on Render deploys on every push to `main`: confirm in Render > Deploys after each merge.
- [ ] Web on Azure Static Web Apps updates after CI passes (see `deploy-web.yml`).
- [ ] Database migrations ran: the API log says migrations applied, and the Super Admin dashboard loads.
- [ ] A Super Admin account exists and its one-time password was replaced. No developer test accounts exist in the live database.
- [ ] Plans: Free and Pro exist. Check their features and limits on the Plans page, and assign Pro to any teacher who should have it.
- [ ] Nightly backup runs: secrets `NEON_DATABASE_URL` and `BACKUP_PASSPHRASE` are set, and one restore was tried (see [backups](backups.md)).
- [ ] `Cors:AllowedOrigins` on the API lists the live web address and nothing else.
- [ ] `Jwt:SigningKey` is a long random value set only in the host's settings, never in the repository.
- [ ] Privacy requests: the Super Admin knows how to download and delete a teacher's data ([privacy](privacy.md)).
- [ ] Check the app on a real mid-range Android phone in light and dark themes ([performance](performance.md)).

## First teachers (pilot)

- [ ] Pick two or three teachers. Create each in Teachers, give them the one-time password, and watch them sign in.
- [ ] Each adds a venue, a class and a few students, marks attendance once, and records one payment.
- [ ] Ask them to read the Help page and note anything unclear.
- [ ] After a week, ask what slowed them down. Record it on the pilot feedback tickets (#50, #62, #73).

## Deferred

- *Deferred:* Azure production environment (S0-6) and the paid hosting plan, until real teachers depend on the app.
- *Deferred:* onboarding teachers beyond the pilot, until the pilot feedback is in.
- *Deferred:* the parent portal (Phase 2). It is a paid-plan feature and is already gated by plan.
- *Built, needs setup:* WhatsApp messages (Sprint 11) are gated to Pro and run in test mode (nothing is sent) until Meta business verification, the three templates and the settings are done: see [WhatsApp setup](whatsapp-setup.md).
- *Built, needs setup:* Drive materials (Sprint 10) is gated to Pro and stays off until the Google client is set up and checked once on the test site: see [Google Drive setup](drive-setup.md).
