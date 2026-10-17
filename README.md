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

## Understanding

Product understanding lives in the separate repo
[musichub-understanding](https://github.com/vanBassum/musichub-understanding),
cloned next to this repo. Open `../musichub.code-workspace` to get both.
Claude treats it as read-only (`.claude/settings.json`).
