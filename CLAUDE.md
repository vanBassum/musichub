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
   - `build` (`.github/workflows/build.yml`): the guard tests, `dotnet build`,
     `pnpm lint` and `pnpm build` of the UI. The Docker image is not built for a
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
     exact head commit. A reviewer job that posts no valid verdict fails.
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
To Do ─label─▶ Analysis ─ready─▶ Build ─PR─▶ Merge ─merged─▶ Done (closed)
                  │                 │  ▲        │
                  │                 │  └─ fix ◀─┤ first review blocked (at most once)
                  └─────────────────┴───────────┴─▶ To Do, label removed, comment why
```

- **Analysis** checks the issue against the understanding projections and admits it
  only as `ready`; otherwise it labels it `autopilot:needs-design`, `:too-large` or
  `:blocked`. It admits by risk, not by technology (`.github/autopilot/analysis.md`
  2.6): API contracts, routing and serving, the build and the image are admitted
  when the understanding supports them and the issue is clear, with acceptance
  criteria for the care they need. Never autonomous: irreversible data loss,
  widening what an audience can see, a security change whose rule the understanding
  doesn't state, a new or upgraded NuGet or npm package or a new shadcn/ui
  component, and changes to `.github/`, `.githooks/`, `.claude/`. Persistence and
  authentication don't exist yet; introducing them is an architecture decision for
  the understanding first.
- **Build** implements only the admitted scope on `autopilot/issue-<N>-r<run>` and
  opens a PR with `Fixes #N`. Its `--allowedTools` list is its whole permission
  policy, since an unattended run can't approve anything. Claude may edit and write
  files, move them with `git mv` and delete them with `git rm`, commit, and run
  `dotnet restore`/`dotnet build` and `pnpm --dir musichub-ui lint|typecheck|build|format`.
  It gets no other shell commands, no package installs, no network and no push.
  `.github/scripts/workflows.test.sh` pins the build's and the fix round's lists,
  and a stop comment names any tool calls the permissions refused. The job then
  checks a clean tree, `dotnet build` and the UI's lint and build itself. Before
  pushing, publish refuses changes to `.github/`, `.githooks/` or `.claude/`, and
  (once EF Core migrations exist under `musichub-api/Data/Migrations`) an edited or
  removed existing migration and a new migration whose `Up()` drops, alters or
  deletes existing data. The PR goes through the same four reviewers and gate.
- **Merge**: the gate also refuses an autopilot PR unless it is its issue's active
  attempt, the issue still has the label and `PIPELINE_MODE` is `full`.
- **Fix round** (at most one per attempt): when the PR's first review blocks it, the
  attempt gets one fix round, but only for findings a code change can address. All
  four reviewer jobs must have succeeded, each with one well-formed verdict for the
  head commit and at least one blocking. The understanding reviewer must not block
  on an UNDOCUMENTED or CONTRADICTED change, which needs Bas, not code. The PR must
  not touch `.github/`, `.githooks/` or `.claude/`, nor carry `no-automerge`, and the
  label and `full` mode must still hold. The attempt then moves to `fix`, where
  nothing merges, and `.github/workflows/autopilot-fix.yml` runs. It checks the
  budgets again. Claude (`.github/autopilot/fix.md`, read-only token) fixes exactly
  the blocking findings, or stops if any of them needs a decision. The same checks
  as publish run over the whole PR, and the fix is pushed to the same PR. All four
  reviewers and the build then run again on the new commit, and the ordinary gate
  decides. The `fix` marker on the issue is the budget: no rerun, duplicate event
  or later review grants a second round. Any other failed review, a second blocked
  review or a failed fix stops the attempt: the PR gets `no-automerge` and is kept
  for investigation. No retries. Adding the label again starts a new attempt, which
  supersedes the old one.
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
report, a failure before the report is posted, or the label being added or removed
during the analysis stops the attempt without posting a report. No branch, commit,
PR or issue is ever made, and an investigation runs the same with `PIPELINE_MODE`
`analysis-only` or `full`. Add `autopilot:analysis-only` before (or together with)
`autopilot`.

Only people with write access can start it (and, if the repository variable
`AUTOPILOT_USERS` is set, only those listed). `PIPELINE_MODE` (repository variable)
is `off` (default), `analysis-only` or `full`. Setting it back to `off` stops a
running attempt at its next step and keeps its PR from merging.

**Budgets**, the only limits on how much autopilot takes on (there is no cap on the
number of issues); a refusal comments why, removes the label and leaves the issue
in To Do. Running reviews and merges are never affected. A fix round is Claude work,
so both budgets are checked again before it; a refusal there stops the attempt.

- *GitHub Actions*: vanBassum's usage this month (all repositories of the account
  share the allowance) plus `AUTOPILOT_ACTIONS_LAG_MINUTES` (60) for not-yet-reported
  usage plus `AUTOPILOT_ATTEMPT_RESERVE_MINUTES` (150) for this and every running
  attempt must stay within `AUTOPILOT_ACTIONS_LIMIT_PCT` (50) of the included
  minutes. Usage comes from the user billing usage API, in Linux-equivalent minutes;
  jobs on the self-hosted runner use none. No API reports the allowance, so it comes
  from the plan (free 2000, pro 3000) unless `AUTOPILOT_ACTIONS_INCLUDED_MINUTES` is
  set. Unreadable data refuses.
- *Claude*: the subscription's session and weekly usage can't be read as a number.
  Claude Code reports a status (its `rate_limit_event`) and warns, with a percentage,
  only when usage is high for that point in the window. Before an attempt, a
  one-line Claude Code probe (Haiku, no tools) reads that status. Autopilot starts
  only on `allowed`, builds only if the analysis run still reported `allowed`, and
  starts a fix round only after a fresh probe reports `allowed`. A warning, a
  rejection, extra-usage credit or no status at all stops it. Each run is also
  capped by `--max-turns` and its job timeout, against runaways. Each comment shows
  the turns, tokens and estimated cost the runs measured (also per reviewer); those
  are measurements, not the remaining allowance. Setting the secret
  `AUTOPILOT_ANTHROPIC_API_KEY` moves analysis and build off the subscription onto
  that key, which a Console workspace spend limit can cap hard.

**Tokens**: `AUTOPILOT_PAT` (secret) is a fine-grained personal access token of
vanBassum. The repository belongs to a user, so a GitHub App token can't reach the
board or the billing usage. It is passed only to the shell steps that need it: push
the branch and open the PR, merge a PR that passed the gate, or read the billing
usage. A fine-grained token can't reach a user's project either, so the card is
moved with `AUTOPILOT_BOARD_TOKEN` (secret), a classic token with only the
`project` scope. It is needed because a PR opened or merged with `GITHUB_TOKEN`
starts no workflows (a merge would never be built or published). Claude never runs
in a job that holds either; `workflows.test.sh` checks that no Claude job references
them.

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
  needs bash, git, jq and Docker.
- The UI calls the API with relative `/api/...` paths: Vite proxies them in
  development, and in the image the API serves the UI itself.
