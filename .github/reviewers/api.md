# Focus: the API, its contract and its correctness

You check the changes under `musichub-api/` and everything else that decides what the
image runs: `Program.cs`, the project files, `appsettings*.json`, the `Dockerfile`. The
UI under `musichub-ui/` is the ui reviewer's, except where it calls the API.

Background: ASP.NET Core 10 with controllers (`AddControllers`, attribute routing
under `/api/*`) and OpenAPI in development. `Program.cs` serves the built UI from
`wwwroot` (copied in by the `Dockerfile`), returns 404 for any unknown `/api` path,
and sends every other path to `index.html` for client-side routing. Data lives in
PostgreSQL through EF Core (`musichub-api/Data/`: the `DbContext`, migrations applied
only by the `--migrate` mode), files in S3-compatible storage through `IFileStorage`
(`musichub-api/Storage/`). There is no authentication yet.

Look for:

- **Logic bugs.** Off-by-one errors, wrong conditions, null dereferences, unhandled
  edge cases (empty input, a missing id, an empty list), date/time and time-zone
  mistakes, and `async` calls that aren't awaited.
- **The API contract.** New endpoints live under `/api/...`, use the HTTP method
  that matches what they do (no state change on GET), validate their input and
  return the status code that fits (400 for bad input, 404 for a missing resource,
  not 200 with an error in the body or a 500 from an unhandled exception). A
  changed response shape of an existing endpoint is `[risk]` unless its callers in
  `musichub-ui/` change with it.
- **Thin controllers.** A controller parses the request, calls a service and
  returns a result. New business rules in a controller belong in a service
  registered in `Program.cs`. Watch for a second way to do something that already
  exists as a service.
- **Routing and serving.** A change that lets a UI path reach the API or an API path
  fall through to `index.html`, breaks the `/api` 404 fallback, or serves files from
  outside `wwwroot`.
- **Dependency injection and lifetimes.** A scoped or transient service captured by
  a singleton, state kept in a singleton that is not thread-safe, `IDisposable`
  resources that are never disposed, `HttpClient` created per call instead of
  through `IHttpClientFactory`.
- **Cancellation and blocking.** Long-running work that ignores the request's
  `CancellationToken`, and `.Result` / `.Wait()` on async code.
- **Configuration and secrets.** Secrets, tokens or connection strings in
  `appsettings*.json` or code; configuration read in a way that silently falls back
  to a wrong default in production.
- **Injection and files.** User input in file paths (path traversal), in shell
  commands, in raw SQL, or written unescaped into HTML.
- **The image.** A `Dockerfile` change that breaks the multi-stage build, runs as
  root instead of `$APP_UID`, or leaves the UI out of `wwwroot`.
- **Persistence.** Introducing a database, ORM or file storage is an architectural
  decision; the understanding reviewer judges whether it is allowed. Once EF Core
  migrations exist (`musichub-api/Data/Migrations`): model changes without a
  migration, an edited existing migration, a migration whose `Up()` does more than
  the PR describes or drops, alters or deletes existing data (`[risk]`), and a
  `Down()` that doesn't reverse it.

## Dependency review

Necessary NuGet packages for an explicitly approved capability may be added in
its implementation PR; a separate human-authored dependency PR is not required.
Check the package's actual purpose, compatible pinned version, supported source,
transitive exposure, and whether an existing dependency already provides the
functionality. Flag unnecessary, suspicious, unmaintained or incompatible
packages as [risk], not the mere presence of a new PackageReference. For EF Core,
check runtime/provider/design package compatibility and that migrations never
run during normal application startup.
