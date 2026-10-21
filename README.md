# MusicHub

One repo, one Docker image: the ASP.NET Core API (`musichub-api`) serves the
built Vite/React UI (`musichub-ui`) from `wwwroot`.

| Path | What |
| --- | --- |
| `musichub-api/` | .NET 10 Web API, controllers under `/api/*` |
| `musichub-ui/` | Vite + React + TypeScript + shadcn/ui (pnpm) |
| `Dockerfile` | Builds the UI, publishes the API, combines both in one runtime image |

## Development

Run both side by side; Vite proxies `/api` to the API.

```bash
cd musichub-api && dotnet run      # http://localhost:5167
cd musichub-ui && pnpm install && pnpm dev   # http://localhost:5173
```

## Docker

```bash
docker build -t musichub .
docker run -p 8080:8080 musichub   # http://localhost:8080
```

Or `docker compose up --build`.

Routing in the container: `/api/*` goes to the controllers (unknown API routes
return 404), every other path falls back to `index.html` for client-side routing.

## Database

The API uses PostgreSQL through EF Core. Configure it with these keys (environment
variables, or the `Database` section of the configuration); no credentials live in
`appsettings*.json`:

| Key | Meaning |
| --- | --- |
| `Database__Host` | PostgreSQL host |
| `Database__Port` | Port (default `5432`) |
| `Database__Database` | Database name |
| `Database__Username` | User |
| `Database__Password` | Password |

The API starts without a reachable database (or without these keys); the readiness
check then reports unhealthy.

**Migrations** are applied by the API itself on every start. Only pending migrations
run, so a start with nothing pending changes nothing. Until they have run, `/api/*`
answers 503 and `/health/ready` is unhealthy (liveness stays healthy, the UI is served).

- An unreachable database is retried without a limit: 5, 10, 20, 40, then every 60
  seconds, each with up to 20% jitter.
- Any other migration failure (a SQL or schema error) is logged as critical and stops
  the API with a non-zero exit code. Fix the cause and restart.
- Instances starting together take turns: Npgsql holds a PostgreSQL advisory lock
  around the migration.
- Migrations are additive only. An older instance still running during a rollout keeps
  using the old schema, so a migration must not break the previous version.

`--migrate` runs the same migration once, without retries, and exits (non-zero on
failure, without printing connection details). It is no longer required:

```bash
docker run --rm -e Database__Host=… -e Database__Database=… -e Database__Username=… -e Database__Password=… <image> --migrate
dotnet MusicHub.Api.dll --migrate   # without Docker
```

**Background jobs** run on Hangfire, stored in the same PostgreSQL database in its own
`hangfire` schema (created on first start). The server starts after the migrations; jobs
queued in PostgreSQL survive a restart, and a stopping instance lets running jobs finish
(up to `ShutdownTimeoutSeconds`). There is no dashboard and no HTTP endpoint for jobs.
Optional keys (the `Hangfire` section):

| Key | Default | Meaning |
| --- | --- | --- |
| `Hangfire__WorkerCount` | `2` | Concurrent jobs (1–32) |
| `Hangfire__RetryAttempts` | `3` | Retries of a failed job (0–20) |
| `Hangfire__RetentionHours` | `168` | How long finished jobs are kept |
| `Hangfire__ShutdownTimeoutSeconds` | `15` | Wait for running jobs on stop (1–25) |
| `Hangfire__SchemaName` | `hangfire` | PostgreSQL schema |
| `Hangfire__CatalogReconciliationCron` | empty (off) | Five-field cron expression, in UTC, that runs "Synchronize catalog" on a schedule (`0 3 * * *` = daily 03:00). A scheduled run is skipped while a run is active. An invalid expression leaves the task unscheduled and is shown on the Jobs page |

The PostgreSQL persistence test runs only when `MUSICHUB_TEST_POSTGRES` holds the
connection string of a disposable database; otherwise it is reported as skipped.

**Health endpoints** (anonymous, JSON body with `status` and per-check `name`/`status`,
no details):

| Route | Runs | Result |
| --- | --- | --- |
| `/health/live` | no checks | always 200 `Healthy` |
| `/health/ready` | the database check | 200, or 503 when the database is unreachable |
| `/health` | all checks | 200, or 503 when the database is unreachable |

The stack's liveness and restart check must use `/health/live`; `/health/ready` and
`/health` are for routing and monitoring only, so a database outage doesn't restart
the API.

## File storage

Files live in an S3-compatible store (MinIO). The API reaches it through `IFileStorage`
(`musichub-api/Storage/`): `PutAsync`, `OpenReadAsync`, `ExistsAsync` and `DeleteAsync`,
all async with a `CancellationToken`. Configure it with these environment variables
(the `FileStorage` configuration section):

| Key | Example |
| --- | --- |
| `FileStorage__Endpoint` | `http://minio:9000` (absolute http(s) URL) |
| `FileStorage__Bucket` | `musichub` |
| `FileStorage__AccessKey` | the access key of the stack's service account |
| `FileStorage__SecretKey` | the secret key of that account |

The stack provisions the bucket and the credentials; the API creates neither. The keys
are checked when storage is first used, not at startup, so the app also starts without
them; a missing or invalid key then fails with an error naming it (never its value).

- Reading a missing key throws `FileNotFoundException`; the caller disposes the returned stream.
- Deleting a missing key succeeds.
- `PutAsync` streams the content and does not dispose the stream it is given.

To verify it, a consumer or test injects `IFileStorage` and does a put, exists, get and
delete round trip against a throwaway bucket. Never use production data or the NAS
source collection for that.

## AcoustID audio fingerprints

The audio fingerprint evidence tool (`source` `acoustid`) makes a Chromaprint fingerprint
with `fpcalc` and asks [AcoustID](https://acoustid.org) for matching recordings. It is
registered only when an API key is configured (the `AcoustId` configuration section):

| Key | Default |
| --- | --- |
| `AcoustId__ApiKey` | none; the application's AcoustID key (secret, never logged) |
| `AcoustId__FpcalcPath` | `fpcalc` |
| `AcoustId__TimeoutSeconds` | `30` (each for `fpcalc` and the AcoustID request) |
| `AcoustId__FingerprintSeconds` | `120` (audio analysed by `fpcalc`) |
| `AcoustId__MaxFileMegabytes` | `200` |
| `AcoustId__MaxConcurrentFingerprints` | `2` |
| `AcoustId__MinScore` | `0.8` (lower AcoustID scores are ignored) |

Runtime dependency: `fpcalc` (Debian package `libchromaprint-tools`), installed in the
Docker image; a local run needs it on the PATH. Without it, without a key, with
unreadable audio, on a timeout or a rate limit the tool reports itself unavailable in the
evidence and yields no candidate. AcoustID scores appear on the evidence as provenance;
they are not MusicHub's identification confidence.

## Acquisition provider (slskd)

Music is found and downloaded through `IAcquisitionProvider` (`musichub-api/Acquisition/`):
`SearchAsync` returns structured candidates (source, path, size, format, bitrate, slots)
and `DownloadAsync` returns an outcome: the downloaded file, or a failure kind with a
detail that says what to check. The interface knows nothing about the catalog, ranking or
identification. `AcquisitionHandoff` gives a downloaded file to the existing ingestion
(source `acquisition:<provider>`); identification then treats it like any stored file, and
a file name or a peer's tags never decide what a file is. Nothing here runs by itself.

slskd, the Docker-hosted Soulseek client, is the first provider. It is registered only
when `Slskd__BaseUrl` is set (the `Slskd` configuration section):

| Key | Default |
| --- | --- |
| `Slskd__BaseUrl` | none; the slskd API, for example `http://slskd:5030` |
| `Slskd__ApiKey` | none; an slskd API key (secret, sent as `X-API-Key`, never logged) |
| `Slskd__DownloadsPath` | none; slskd's downloads folder as MusicHub sees it |
| `Slskd__SearchTimeoutSeconds` | `15` |
| `Slskd__MaxCandidates` | `50` |
| `Slskd__RequestTimeoutSeconds` | `30` (each request to slskd) |
| `Slskd__DownloadTimeoutSeconds` | `600` (queued until finished; then cancelled in slskd) |
| `Slskd__PollIntervalMs` | `1000` |
| `Slskd__RateLimitMaxRetries` | `4` (repeats of a request slskd answered with 429) |
| `Slskd__RateLimitBaseDelayMs` | `1000` (first wait without `Retry-After`; doubles) |
| `Slskd__RateLimitMaxDelayMs` | `30000` (longest wait between attempts) |

slskd's API doesn't serve downloaded files, so the stack mounts slskd's downloads folder
read-only into the API container and points `Slskd__DownloadsPath` at it; without it a
finished download is reported as not accessible. A missing `Slskd__ApiKey` or an invalid
setting fails with an error naming the key.

## Search for Missing Music

A manual task in Jobs (`POST /api/jobs/missing-music-acquisition/start`; refused with 409
while a run is active or no provider is configured). It never runs by itself. Its scope is
the library's Missing Music: every track without a Stored file, not `WantedAt`. Per track
(`TrackAcquisition`):

1. Search through `IAcquisitionProvider` (the results are transient: no search table).
2. Rank (`CandidateRanking`): only MP3s whose name claims the title and an artist, with no
   marker of another version (live, remix, ...) the track doesn't carry. The right
   recording comes before bitrate; among those, the higher bitrate, then a free slot. A
   lower bitrate is only tried later, never excluded.
3. Skip candidates (same provider, source and path) already rejected
   (`AcquisitionHistory`); a failed or cancelled transfer judged nothing and is retried.
4. Download to the provider's download area (temporary disk, not MinIO), hash the bytes
   (full-file SHA-256 and `mp3-payload-sha256-v1`, the SHA-256 of the audio without ID3v2/
   ID3v1/APEv2 tags; the rules are in `AudioHashing`). Equal bytes already rejected for the
   track are rejected again without identifying.
5. Identify with the existing `IRecordingIdentifier`. Only an identification of the same
   title and artists (`RecordingMatch`) imports the file: through `AcquisitionHandoff`
   into the existing ingestion (the bytes must match the hashes), then linked to the
   track in one transaction with its `AppliedIdentification`. A track with a version is
   never confirmed (an identification names no version). Anything else stays outside
   MinIO, recorded as rejected with its reason; the file stays in the download area
   (cleanup, retention and automatic re-evaluation are not built).

Every attempted download is an `AcquisitionDownload` row, written through
`AcquisitionHistory` (kept for imports too: provider, source, source path, state and
outcome, reason, hashes, algorithm, intended track, file, job execution, timestamps). Every
identification of a download is a permanent `IdentificationOutcome` with `DownloadId`,
its methods and `PipelineVersion` (the build's commit, which versions the methods).
Jobs shows the run's progress, per-track results, downloads and identification attempts.
The job isn't retried by Hangfire; stopping it marks the transfer cancelled, and a new run
starts over with what is still missing. Existing files and links are never changed.

| Key | Default |
| --- | --- |
| `MissingMusic__MaxConcurrentTracks` | `2` (tracks searched and downloaded at the same time) |
| `MissingMusic__MaxDownloadsPerTrack` | `3` (candidates downloaded per track per run, best first) |

## Understanding

Product understanding lives in the separate repo
[musichub-understanding](https://github.com/vanBassum/musichub-understanding),
cloned next to this repo. Open `../musichub.code-workspace` to get both.
Claude treats it as read-only (`.claude/settings.json`, `.claude/hooks/guard.js`).

## How changes reach `main`

Never push to `main`; GitHub refuses it (ruleset "Protect main"). Each clone also
guards it with a local hook. Turn it on once per clone:

```bash
git config core.hooksPath .githooks   # refuses pushes to main
```

1. Branch from an up-to-date `main` (`feature/…`, `fix/…`, `docs/…`, `ci/…`), push
   it, and open a pull request to `main`.
2. The build and four Claude reviewers (api, ui, understanding, smells) run on every
   push. Each reviewer posts its findings and a verdict for the exact commit; its
   check is green only without blocking findings.
3. A PR that Claude opened from an `autopilot` issue merges by itself once
   everything is green (GitHub auto-merge). Bas merges every other PR. The ruleset
   "Protect main" enforces the checks; `.github/CODEOWNERS` keeps pipeline changes
   with Bas.

The `autopilot` label on an issue lets Claude build it and open the PR, or say which
decision is missing. `@claude` in a comment on an issue or a PR asks Claude for
anything else, such as "@claude fix the review findings". Use the "Work for
autopilot" issue form.

Every push to `main` publishes `ghcr.io/vanbassum/musichub:sha-<12 hex>` (and the
moving `:main`). Environments pin a `sha-…` tag; there are no release tags.

The full rules, also for Claude sessions, are in [CLAUDE.md](CLAUDE.md).
