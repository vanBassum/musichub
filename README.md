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

## Understanding

Product understanding lives in the separate repo
[musichub-understanding](https://github.com/vanBassum/musichub-understanding),
cloned next to this repo. Open `../musichub.code-workspace` to get both.
Claude treats it as read-only (`.claude/settings.json`, `.claude/hooks/guard.js`).

## How changes reach `main`

Never push to `main`. GitHub can't protect it on this plan, so each clone guards it
with a local hook. Turn it on once per clone:

```bash
git config core.hooksPath .githooks   # refuses pushes to main
```

1. Branch from an up-to-date `main` (`feature/…`, `fix/…`, `docs/…`, `ci/…`), push
   it, and open a pull request to `main`.
2. The build and four Claude reviewers (api, ui, understanding, smells) run on every
   push. Each reviewer posts its findings and a verdict for the exact commit.
3. When the build passes and no reviewer reports a blocking finding, the PR is
   squash-merged automatically. A PR that changes `.github/`, `.githooks/` or
   `.claude/`, or carries the `no-automerge` label, is merged by hand.

The `autopilot` label on an issue lets Claude analyse, build and open the PR by
itself; `autopilot:analysis-only` together with it asks for an investigation report
only.

Every push to `main` publishes `ghcr.io/vanbassum/musichub:sha-<12 hex>` (and the
moving `:main`). Environments pin a `sha-…` tag; there are no release tags.

The full rules, also for Claude sessions, are in [CLAUDE.md](CLAUDE.md).
