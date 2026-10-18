# musichub — project guidance for Claude

One repository, one Docker image: the ASP.NET Core 10 Web API (`musichub-api/`)
serves the built Vite + React UI (`musichub-ui/`) from `wwwroot`. See `README.md` for
running it.

## Git workflow — read this first

These rules apply to everyone who works on this repo, and to every Claude session.

### 1. Never push to `main` directly

GitHub can't protect `main` here: the repository is private on a free plan. Never
commit to `main`, never push to `main`, never force-push `main`. Each clone guards it
locally instead:

- `.githooks/pre-push` refuses every push to `main`. Turn it on once per clone with
  `git config core.hooksPath .githooks` (the README says so too). Claude sessions turn
  it on automatically at session start (`.claude/settings.json`).
- `.claude/hooks/guard.js` (a PreToolUse hook in the shared `.claude/settings.json`)
  refuses, for Claude: pushes to `main`, `--no-verify`, changing `core.hooksPath`,
  and any write to musichub-understanding. Tested by `.claude/hooks/guard.test.js`.
- Personal Claude permissions go in `.claude/settings.local.json`, which is not
  versioned.

Never bypass these guards (`--no-verify`, another hooks path, editing them to get
something through).

### 2. All work goes through a pull request

1. Start from an up-to-date `main` and create a branch: `feature/…`, `fix/…`,
   `docs/…` or `ci/…` (`git fetch origin && git switch -c feature/x origin/main`).
2. Commit, push the branch and open a PR to `main` (`gh pr create --base main`). The
   description states the user's problem in a sentence or two, `Fixes #N`, and for a
   feature a **Builds on** section: the existing capabilities it uses, and why
   anything new was needed. The reviewers can't open the issue, so the problem has to
   be in the PR. While the work is unfinished, open it as a draft: drafts are not
   reviewed.
3. The PR is checked automatically:
   - `build` (`.github/workflows/build.yml`): the guard tests, and `dotnet build`,
     `dotnet test`, `pnpm lint` and `pnpm build` of the UI, in the sandbox. The Docker image is not built for a
     PR, only after the merge to `main`. The reviewers never build anything
     themselves: that is the pipeline's job.
   - Four Claude reviewers (`.github/workflows/claude-review.yml`), each with its own
     instructions in `.github/reviewers/`: **api** (the .NET API, its contract,
     routing and serving, the image), **ui** (React, shadcn/ui components, styling,
     state), **understanding** (is the change allowed by the projections in
     musichub-understanding; does it compose existing capabilities instead of
     building beside them; security, the review gate) and **smells** (code smells in
     the new code, from the refactoring.guru catalog). They post inline comments plus
     one summary comment each, ending in a
     `<!-- claude-review: <reviewer> sha=<SHA> blocking=<N> -->` verdict for the
     exact head commit (the understanding reviewer's also says `decisions=<D>`: how
     many of its blocking findings need a decision rather than a code change). Each
     blocking finding carries a key, `<!-- finding: <reviewer>:<path>:<slug> -->`. A
     reviewer job that posts no valid verdict fails; the watchdog reruns it once.
4. The reviewers run by themselves when a PR is opened, reopened or marked ready for
   review, and again on every push. A newer push cancels the run in progress, and
   verdicts only count for the commit they name. There is nothing to trigger by
   hand. The workflow runs on `pull_request_target`: the workflow, the reviewer
   rules and the merge gate always come from `main`, so a PR that changes them is
   reviewed and gated by the old versions. Drafts and PRs from forks are not
   reviewed.
5. **The `automerge` job squash-merges the PR into `main` and deletes the branch**
   when the gate in `.github/scripts/review-gate.sh` passes. It merges only when:
   all four reviewer jobs succeeded; each reviewer posted exactly one well-formed
   verdict (or identical repeats) for the PR's current head SHA with `blocking=0`;
   the `build` check on that SHA passed; the PR doesn't carry the `no-automerge`
   label; and it changes nothing under `.github/`, `.githooks/` or `.claude/`.
   Anything missing, malformed, contradictory, stale, failed or unreadable refuses
   the merge. Blocking findings are `[bug]`, `[risk]` and `[smell]`; `[convention]`
   findings don't block. A green `automerge` check means merged; red means the PR
   needs attention. The gate's cases are tested by
   `.github/scripts/review-gate.test.sh` (it runs in CI).

   **Trust boundary:** a PR that changes anything under `.github/` (workflows,
   reviewer rules, the gate), `.githooks/` or `.claude/` (the local guards) never
   merges itself, and Claude never merges it by hand either. The reviewers still
   run; then the gate fails with "Bas merges it by hand". Put such changes in their
   own PR, so ordinary work isn't held up by them.

### After opening a PR

Claude sessions may commit, push the branch and open the PR without asking first;
the merge is the gate's job. Wait for the result with `gh pr checks <PR> --watch`,
then:

- **Green `automerge`**: the PR is merged. Confirm with `gh pr view <PR> --json state`,
  then switch back to `main`, `git pull`, `git fetch --prune`, and delete only your
  local feature branch (`git branch -D <branch>`).
- **Red because of a blocking finding**: fix it and push; the reviewers run again by
  themselves. At most two reasonable fix attempts.
- **Red because a reviewer job failed or left no verdict** (error, timeout, missing
  marker): rerun it with `gh run rerun <run-id> --failed`.
- **Red because of the trust boundary or `no-automerge`**: done. Tell Bas the PR is
  reviewed and waiting for his merge.

Never get past a blocking finding or the gate yourself: don't merge by hand, don't
reply a finding away, don't add or remove labels to steer the gate, and never edit
the workflow, the gate or the reviewer rules to get a PR through. If a blocking
finding still stands after two attempts, or you believe the finding itself is wrong,
stop and escalate to Bas: link the PR, quote the finding, say what you tried. Leave
the PR open.

### Autopilot: the `autopilot` label

Adding the `autopilot` label to an issue means "Claude, take care of this issue":
one autonomous attempt (`.github/workflows/autopilot.yml`, state machine in
`.github/scripts/autopilot.sh`, prompts in `.github/autopilot/`). The board (a
project of vanBassum, `AUTOPILOT_PROJECT_ID`) follows it:

```text
To Do ─label─▶ Build ─PR─▶ Merge ─merged─▶ Done (closed)
  ▲   (waits)    │   ▲        │
  │              │   └─ fix ◀─┤ review blocked: up to 3 fix rounds
  └──────────────┴────────────┴─▶ To Do, label removed, comment why
```

- **Build, in one step.** One Claude run (`.github/autopilot/build.md`) first judges
  the issue against the understanding and the admission rules
  (`.github/autopilot/admission.md`), and only when it may be built, builds it on
  `autopilot/issue-<N>-r<run>`. Otherwise it labels the issue
  `autopilot:needs-design`, `:too-large` or `:blocked` and says what would make it
  ready. It admits by risk, not by technology (`admission.md` 2.6): API contracts,
  routing and serving, the build and the image, the database and file storage, and
  schema changes are admitted when the understanding (its projections and the
  accepted logs newer than them) supports them and the issue is clear, with
  acceptance criteria for the care they need. Never autonomous: irreversible data
  loss, widening what an audience can see, a security change whose rule the
  understanding doesn't state, an unapproved, unjustified or unsafe package, and
  changes to `.github/`, `.githooks/`, `.claude/`. Authentication doesn't exist yet;
  introducing it is an architecture decision for the understanding first.
- **The sandbox.** Code that no person has merged never runs on the runner: Claude's
  build and fix runs, and the PR's own restore, build, tests and lint (the `test`
  job of `build.yml`), run in a throwaway container (`.github/scripts/sandbox.sh`,
  image `.github/sandbox/Dockerfile`): no Docker socket, no runner files, no GitHub
  token, no PAT, a read-only root filesystem, its own Docker network. The job's
  trusted steps put the issue, the open PRs, the understanding and the issue's
  images into it, and read back only regular files. Claude's `--allowedTools` list
  (in `.github/scripts/sandbox-job.sh`, pinned by `workflows.test.sh`) is its whole
  permission policy: edit and write files, `git mv`/`git rm`, commit, `dotnet
  restore`/`build`/`ef`, `pnpm --dir musichub-ui lint|typecheck|build|format`, and two
  scripts: `shadcn-components.sh` (official shadcn/ui components by name, no new
  package, only new files in `src/components/ui/`) and `add-package.sh` (a NuGet or
  npm package by name at an exact version, `pnpm-lock.yaml` included; no upgrade).
  No test suite: the understanding has implementation agents address test failures
  from CI results, so the pipeline runs the tests and a failure comes back as a fix
  round. No GitHub access and no push.
- **Publish** pushes the commits and opens the PR with `Fixes #N`. It stops on
  uncommitted changes; a failed build, test or lint in the pipeline's own check
  still becomes a PR, whose build check fails the same way and gets a fix round. It
  refuses changes to `.github/`, `.githooks/` or `.claude/`, an edited or removed
  existing migration, and a new migration whose `Up()` drops, alters or deletes
  existing data. The PR goes through the same four reviewers and gate.
- **Merge**: the gate also refuses an autopilot PR unless it is its issue's active
  attempt, the issue still has the label and `PIPELINE_MODE` is `full`.
- **Fix rounds** (up to three per attempt): when a review blocks the PR, the attempt
  gets a fix round for what a code change can fix: blocking reviewer findings, a
  failed build check (Claude gets the failed jobs' logs) and a conflict with main
  (main is merged in first). All four reviewer jobs must have succeeded, each with
  one well-formed verdict for the head commit. The attempt stops earlier when a
  finding needs a decision (the understanding reviewer's `decisions=` is above 0:
  UNDOCUMENTED, CONTRADICTED, or a change autopilot may not make on its own), when
  a finding comes back that the previous round reported fixed in a file it changed
  (the reviewers mark each blocking finding with a key, `<!-- finding: … -->`; a
  failure of the build check gets one from its log), when Claude finds nothing left
  it can fix, or when Claude's usage doesn't allow it. A new or different finding is
  progress and gets the next round. Claude (`.github/autopilot/fix.md`) fixes what
  it can and reports each key as `Fixed:` or `Needs decision:`. The same checks as
  publish run over the whole PR, and the fix is pushed to the same PR; all four
  reviewers and the build check run again. The `fix` markers on the issue are the
  budget: no rerun, duplicate event or later review grants an extra round. While the
  attempt is in `fix`, nothing merges. A stopped attempt's PR gets `no-automerge`
  and is kept.
- **Retries**: a job that fails (a crash, a timeout, a reviewer that left no verdict)
  is rerun once by `.github/workflows/autopilot-watchdog.yml`; when it fails again,
  the attempt stops with the jobs that failed. Nothing hangs silently. What Claude
  decided is never retried.
- **Resuming**: adding the label again while the stopped attempt's PR is still open
  resumes that attempt on that PR: main is merged into it (or it is marked ready
  again), it gets a full new review and three new fix rounds, and `no-automerge` is
  removed (not when a person added it). Nothing is rebuilt. To start over instead,
  close the PR first. Otherwise a new attempt supersedes any earlier one.
- **Images**: build and fix see the images attached to the issue and its comments.
  Before Claude runs, `.github/scripts/issue-images.sh` downloads them with the
  job's read-only `GITHUB_TOKEN` (through the signed URLs in the API's rendered
  HTML), with an `index.md` saying where each one is. Only GitHub's own attachment
  hosts are fetched; an external image is listed, not downloaded.
- **Issues**: the issue form "Work for autopilot" (`.github/ISSUE_TEMPLATE/`) asks for
  what the build checks first: acceptance criteria, the understanding that supports
  the change, exposure, packages and components.
- Moving a card on the board starts nothing. Issues without the label stay manual.

**Investigations: `autopilot:analysis-only`.** An issue that carries this label as
well as `autopilot` is investigated, not built: `To Do ─label─▶ Analysis ─report─▶ To Do`.
The analysis step follows `.github/autopilot/investigation.md`. It reads the code and
the understanding, and writes a report (summary, current implementation, findings
marked verified or assumed, risks and dependencies, implementation sequence,
suggested follow-up work, open questions). The workflow posts the report as a
comment on the issue, and that is all: it creates no issues, doesn't close the
issue, removes `autopilot` (one shot; `autopilot:analysis-only` stays) and moves the
card back to To Do, so the issue is open for discussion. Follow-up work appears only
as prose suggestions in the report; Bas decides which of it becomes an issue. A
result other than `complete` also gets its `autopilot:<result>` label. A malformed
report, a failure before the report is posted, or the label being removed during
the analysis stops the attempt without posting a report. No branch, commit, PR or
issue is ever made, and an investigation runs the same with `PIPELINE_MODE`
`analysis-only` or `full`. Add `autopilot:analysis-only` before (or together with)
`autopilot`.

Only people with write access can start it (and, if the repository variable
`AUTOPILOT_USERS` is set, only those listed). `PIPELINE_MODE` (repository variable)
is `off` (default), `analysis-only` (Claude judges the issue and says whether it
would build it; nothing is built) or `full`. Setting it back to `off` stops a
running attempt at its next step and keeps its PR from merging.

**Budget**, the only limit on how much autopilot takes on (there is no cap on the
number of issues). Running reviews and merges are never affected. Every job runs on
the self-hosted runner, which uses no GitHub Actions minutes, so there is no
Actions budget.

- *Claude*: the subscription's session and weekly usage can't be read as a number.
  Claude Code reports a status (its `rate_limit_event`) and warns, with a percentage,
  only when usage is high for that point in the window. Before an attempt and
  before each fix round, a one-line Claude Code probe (Haiku, no tools) reads that
  status. Autopilot goes on only on `allowed`. A warning, a rejection, extra-usage
  credit or no status at all makes a new attempt **wait**: the issue keeps its label
  (stage `waiting`, card in To Do) and `.github/workflows/autopilot-queue.yml`
  starts it again by itself (every 30 minutes, one issue per tick, through
  autopilot.yml's `workflow_dispatch`). Before a fix round it stops the attempt
  (resume it later by adding the label again). The label
  `autopilot:ignore-claude-warning` on the issue lets a warning through (not a
  rejection). Each run is also capped by `--max-turns` and a timeout, against
  runaways. Each comment shows the turns, tokens and estimated cost the runs
  measured (also per reviewer); those are measurements, not the remaining
  allowance. Setting the secret `AUTOPILOT_ANTHROPIC_API_KEY` moves build, fix and
  investigation off the subscription onto that key, which a Console workspace spend
  limit can cap hard.

**Tokens**: `AUTOPILOT_PAT` (secret) is a fine-grained personal access token of
vanBassum. The repository belongs to a user, so a GitHub App token can't reach the
board. It is passed only to the shell steps that need it: push the branch and open
the PR, resume a PR, merge a PR that passed the gate, rerun a failed run (the
watchdog), and dispatch a fix round or a queued attempt. Both need `Actions: read
and write`: a run started with `GITHUB_TOKEN` starts no `workflow_run`, so the
watchdog would never see it fail. A fine-grained token can't reach a user's project either, so the card
is moved with `AUTOPILOT_BOARD_TOKEN` (secret), a classic token with the `project`
and `repo` scopes (`project` alone can't add an issue of this private repository;
set to expire, renew it then). It is needed because a PR opened or merged with
`GITHUB_TOKEN` starts no workflows (a merge would never be built or published).
Claude never runs in a job that holds either, and no job that holds either runs the
sandbox or checks out a PR's code; `workflows.test.sh` checks both.

Claude sessions don't add or remove the `autopilot` label unless Bas asks.

### musichub-understanding is read-only for Claude

`vanBassum/musichub-understanding` (checked out next to this repo,
`C:\Workspace\musichub-understanding`; `../musichub.code-workspace` opens both) is
the source of truth that the understanding reviewer checks code against. Claude may
**read** it, but **never writes to it**: no files, commits, branches, pushes, PRs or
GitHub API writes, not even a "small" log entry. If Claude changed the understanding
and the code together, the review would be checking the code against Claude's own
plan.

When work needs a change in the understanding (a missing rule, a contradiction, an
`UNDOCUMENTED` reviewer finding), **stop and tell Bas**: what is missing, why the
work needs it, and a proposed wording. Bas either changes the understanding or
redirects the work. Meanwhile the code PR waits (draft) or is reshaped to fit the
current understanding; never route around it.

The PR review and autopilot read it through a read-only deploy key (secret
`UNDERSTANDING_DEPLOY_KEY`). On every machine, the deny rules in
`.claude/settings.json` and `.claude/hooks/guard.js` refuse Claude's file edits and
writing shell or GitHub commands against it.

### 3. Images: one per commit on `main`, no release tags

Every push to `main` (so every merged PR) runs `.github/workflows/build.yml`, which
publishes one immutable image tagged with the commit it was built from:
`ghcr.io/vanbassum/musichub:sha-<12 hex>`, plus the moving alias `:main`. A sha tag
is written once; a rerun for a commit that is already published doesn't replace it.
The image carries its build identity as `APP_VERSION` (the commit date) and
`APP_COMMIT` (the 12-character SHA).

An environment runs a pinned `sha-…` tag, never `:main`. Deploying, promoting and
rolling back are all "change the pinned tag", never a rebuild. There are no release
tags and no release branches. The package is private: a server that pulls it needs
`docker login ghcr.io` with a token that can read packages.

The one exception is acceptance (https://musichub-acc.vanbassum.com, stack
`stacks/musichub-acc` in strato-stack), which follows `:main` on purpose. After each
main build, the `deploy-acceptance` job in `build.yml` runs
`.github/scripts/deploy-acceptance.sh` on the strato runner. It pulls the commit's
sha tag with the job's token, points the host's `:main` at it, and has that stack's
Watchtower recreate the container. No registry credential lives on that host.

## Build / run notes

- Targets **.NET 10** (`MusicHub.slnx`) and Node 22 with pnpm 10 (`musichub-ui/`).
- CI and autopilot run on the self-hosted runner (`[self-hosted, strato]`), which
  needs bash, git, jq and Docker. The PR checks and autopilot's Claude runs happen
  in the sandbox container (`.github/sandbox/Dockerfile`, built on the runner the
  first time it is needed).
- The UI calls the API with relative `/api/...` paths: Vite proxies them in
  development, and in the image the API serves the UI itself.
