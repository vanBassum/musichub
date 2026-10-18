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
     `dotnet test`, `pnpm lint` and `pnpm build` of the UI, in the sandbox. The
     Docker image is not built for a PR, only after the merge to `main`. The
     reviewers never build anything themselves: that is the pipeline's job.
   - Four Claude reviewers (`.github/workflows/claude-review.yml`), each with its own
     instructions in `.github/reviewers/`: **api** (the .NET API, its contract,
     routing and serving, the image), **ui** (React, shadcn/ui components, styling,
     state), **understanding** (is the change allowed by the projections in
     musichub-understanding; does it compose existing capabilities instead of
     building beside them; security) and **smells** (code smells in the new code,
     from the refactoring.guru catalog). They post inline comments plus one summary
     comment each, ending in a `<!-- claude-review: <reviewer> sha=<SHA> blocking=<N> -->`
     verdict for the exact head commit. A reviewer's job is green only when it
     passed that commit with `blocking=0`: red means a blocking finding (read its
     comment) or no valid verdict (rerun the job). Blocking findings are `[bug]`,
     `[risk]` and `[smell]`; `[convention]` findings don't block.
4. The reviewers run by themselves when a PR is opened, reopened or marked ready for
   review, and again on every push. A newer push cancels the run in progress. The
   workflow runs on `pull_request_target`: the workflow and the reviewer rules
   always come from `main`, so a PR that changes them is reviewed by the old
   versions. Drafts and PRs from forks are not reviewed.
5. **Bas merges.** Nothing merges by itself, and Claude never merges a PR.

### After opening a PR

Claude sessions may commit, push the branch and open the PR without asking first.
Wait for the result with `gh pr checks <PR> --watch`, then:

- **All green** (`build` and the four reviewers): tell Bas the PR is ready for his
  merge.
- **Red because of a blocking finding**: fix it and push; the reviewers run again by
  themselves. At most two reasonable fix attempts.
- **Red because a reviewer left no verdict** (error, timeout, missing marker): rerun
  it with `gh run rerun <run-id> --failed`.

Never get past a blocking finding yourself: don't reply a finding away and never
edit the workflow or the reviewer rules to get a PR through. If a blocking finding
still stands after two attempts, or you believe the finding itself is wrong, stop
and escalate to Bas: link the PR, quote the finding, say what you tried. Leave the
PR open.

### Claude in GitHub: the `autopilot` label and `@claude`

`.github/workflows/claude.yml` runs the official `anthropics/claude-code-action` on
the self-hosted runner. Claude follows `.github/autopilot.md`.

- **`autopilot` on an issue**: Claude reads the issue and the understanding, builds
  it on a `claude/…` branch and opens the PR. When the work needs a decision that
  the understanding or the issue doesn't make, it changes nothing and says in its
  comment what is missing, with a proposed answer. Adding the label again, or an
  `@claude` comment, starts it again.
- **`@claude` in a comment** on an issue or a PR: Claude does what the comment asks.
  On a PR it pushes to the PR's branch, for example after "@claude fix the review
  findings". Fix rounds start this way, by a person.
- Its PR gets the same build check and four reviewers as any other; Bas merges.
- It authenticates as the Claude GitHub App (OIDC, a short-lived token for this
  repository), so its pushes start the build and the reviewers. Only people with
  write access can start it; bots can't. It uses the subscription through
  `CLAUDE_CODE_OAUTH_TOKEN`.
- Use the issue form "Work for autopilot" (`.github/ISSUE_TEMPLATE/`).

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

The PR review and `claude.yml` read it through a read-only deploy key (secret
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
- CI and Claude run on the self-hosted runner (`[self-hosted, strato]`), which needs
  bash, git, jq and Docker. The PR checks run in the sandbox container
  (`.github/sandbox/Dockerfile`, built on the runner the first time it is needed).
- The UI calls the API with relative `/api/...` paths: Vite proxies them in
  development, and in the image the API serves the UI itself.
