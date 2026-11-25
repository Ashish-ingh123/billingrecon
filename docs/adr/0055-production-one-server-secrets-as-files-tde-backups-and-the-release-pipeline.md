# ADR 0055: Production: one server, secrets as files, TDE, backups and the release pipeline

- **Status:** Accepted. The owner answered the four questions of Prompt 10.5 and approved its plan on 2026-11-25: one
  Linux server with Docker Compose and no cloud assumed, SQL Server 2022 Standard, a certificate of Let's Encrypt for a
  domain named in the host's configuration, and secrets as files on the host.
- **Date:** 2026-11-25
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0017 (the image and CI), ADR 0034 (the logins, `READ_COMMITTED_SNAPSHOT`), ADR 0035 (TLS at the
  proxy), ADR 0045 (the local run this is derived from), ADR 0052 (the operator's commands), ADR 0058 (no client yet).

## Context

R1 runs with one command on a developer's machine, with a self-signed certificate and its secrets in `.env`. A hospital
needs a deployment whose secrets are in no file of git and no environment of a container, whose data is encrypted where
it rests and is held in India, that is backed up and provably restorable, and that is released the same way every time.
There is no client, no server, no domain and no registry yet (ADR 0058): this is written to be run, and has not run.

## Decision

1. **One Linux server in an Indian region, Docker Compose.** `docker-compose.prod.yml` runs SQL Server, Seq, the
   migration step, the API, nginx, certbot, the backups and their off-site copy. Any provider with a region in India,
   or a hospital's own server, runs it; none is assumed. Staging is the same file on another server with its own
   environment file and secrets, `API_ENVIRONMENT=Staging`.
2. **Secrets are files of the host.** `/etc/billingrecon/secrets`, a directory only root may enter, holds one file a
   secret; Compose mounts each into the one service that needs it. The API reads its two, `Database__ConnectionString`
   and `Jwt__SigningKey`, through `SecretFiles`: every file of `/run/secrets` (or `BILLINGRECON_SECRETS_DIR`) is the
   setting of its name, read once at startup, and wins over every other source. A missing or empty one is refused at
   startup by the options validation that was already there. The environment file holds no secret, and an architecture
   test holds the compose file and its example to that.
3. **`READ_COMMITTED_SNAPSHOT` is checked.** Outside Development the API refuses to start on a database that answers and
   has it off (`DatabaseStartup`, through `DatabaseSettingsProbe`), and `/health/ready` is not ready on one. A database
   that does not answer at startup does not stop the start: readiness says so, as before. The probe asks the server's
   catalogue on a connection of its own; it is not one of the files with raw SQL of ADR 0021, as it touches no table of
   the application and goes through no context.
4. **SQL Server 2022 Standard, encrypted by TDE.** The edition is named in the environment file (`MSSQL_PID`), as it
   is a paid licence the hospital holds. `deploy/prod/prepare.sql` turns TDE on with a server certificate and sets the
   full recovery model. The API warns at startup when the database is not encrypted, and does not refuse: a host may
   encrypt the disk instead, which Express would need. The certificate's backup and its password leave the server and
   are kept by the operator; without them no backup can be restored elsewhere.
5. **A certificate of Let's Encrypt.** certbot asks for and renews it over a folder nginx serves on port 80; nginx
   serves a self-signed one until the first exists and takes a new one within a minute. Accepting Let's Encrypt's
   agreement is a line of the environment file, as the two licences are.
6. **Backups.** A full backup nightly and the log every 15 minutes, compressed, each written with a checksum and the
   full one verified, encrypted as the database is, kept 35 days on the server, and copied off it by rclone to a remote
   in India (`copy`, never `sync`, so a deletion here deletes nothing there). The worst loss is 15 minutes plus the copy.
   **A restore drill each month** restores the newest full backup under another name, checks every page, holds the
   ledger to balance, and drops it; its date goes into the runbook.
7. **The release pipeline.** A tag `v*` builds the three images and pushes them to GitHub's registry under that tag,
   then deploys staging: the tag's migration image runs as `billingrecon_migrator` first, and only then the new API
   starts. Production is the same job started by hand with a tag, behind the reviewer of GitHub's `production`
   environment. A server pulls images and never builds. The server's host key is pinned.
8. **Where the data is.** Everything (the database, its backups, the logs in Seq) is on the server and the off-site
   remote, both to be placed in India. Nothing is sent anywhere else; the only outside calls are certbot's and rclone's.

## Consequences

- A rotated secret needs a restart of its service; the runbook has a procedure for each.
- The administrator's password of SQL Server reaches its process as a variable set from the file by the container's
  command, as the image reads nothing else. It is in no file of Compose and no `docker inspect` of the service.
- One server is one point of failure. Recovery is a restore on a new server, hours and not minutes; a second server is
  R5's question.
- Three images of others run in production beside Microsoft's and nginx: certbot, rclone and Seq. Their tags are
  pinned, and are to be checked on the first run.

## Alternatives considered

- **A named cloud and its managed database and vault.** The owner chose no cloud; nothing here prevents one later.
- **Secrets in the environment file.** Simpler, and readable by anyone who may inspect a container.
- **Refusing to start without TDE.** It would bind the product to the Standard edition; a warning leaves the choice of
  disk encryption open and visible.
- **SQL Server Agent jobs for the backups.** They live inside the database server's own state; a loop in a container
  is in git and is the same on every server.

## Built note (2026-11-25)

- `AddKeyPerFile` of ASP.NET Core was tried and left: on Windows it keeps a file's last line feed. `SecretFiles` reads
  the files itself, in a dozen lines, and behaves the same everywhere.
- Verified on the office machine: the build, the format gate, every test outside the `Database` collection, and on
  LocalDB 2019 the refusal to start with the snapshot off. **Not run:** the compose file, certbot, TDE, the backups, the
  drill and the workflow, for want of Docker, a server, a domain and a registry (runbook section 8, row 10.5). The
  prompt's "done when" (staging deployed from a tag, a backup restored, the owner walking the procedures) is open and is
  a line of `docs/go-live.md`.
- The restore drill holds the ledger to balance in SQL. The twelve identities of the reconciliation are run on a
  restored copy by the scheduled reconciliation of Prompt 10.6, which this prompt does not build.
