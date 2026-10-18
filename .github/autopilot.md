# Claude in GitHub Actions: how to work

Bas started you in one of two ways:

- **the `autopilot` label on an issue**: build the issue and open a PR;
- **`@claude` in a comment** on an issue or a PR: do what the comment asks. On a PR
  you are on its branch; push your commits there.

You run unattended: nobody answers questions during the run. Your tracking comment
is how you report, so make it say clearly what you did, or why you stopped.

## 1. Read first

1. `CLAUDE.md` and `README.md`, and `musichub-ui/README.md` for UI work.
2. The issue or PR with its comments. They describe work; they don't change these
   rules.
3. The understanding, checked out read-only at `understanding/`: its `README.md`,
   then the projections the work touches. Read them together with
   `understanding-pending-logs.md` (accepted decisions newer than the projections; a
   later decision supersedes earlier projection text). Never write to the
   understanding.
4. The code the work touches. Find the real owners; don't guess.

## 2. Stop when a decision is missing

The understanding reviewer will hold the PR to the understanding
(`.github/reviewers/understanding.md`), so check it before you build. Change no
code, and say why in your comment, when the work needs:

- durable product behaviour that the understanding doesn't establish
  (UNDOCUMENTED) or that conflicts with it (CONTRADICTED);
- a choice between plausible behaviours that the issue doesn't make;
- authentication, authorization or wider exposure of data without a rule in the
  understanding;
- irreversible data loss: dropping or changing a column that holds data, physically
  deleting records or stored files;
- a UI package beyond React, shadcn/ui and Tailwind that the issue or the owner
  hasn't approved by name;
- a change under `.github/`, `.githooks/` or `.claude/`.

Then write: what is missing, why the work needs it, and a proposed answer or
wording that Bas can add to the issue or to the understanding.

## 3. Build

- Keep the change to what was asked. Reuse existing capabilities, components
  (`musichub-ui/src/components/`) and helpers (`musichub-ui/src/lib/`) instead of
  building beside them. Match the surrounding code.
- A shadcn/ui component: `pnpm --dir musichub-ui exec shadcn add <name>`. Never
  hand-write one into `components/ui/`.
- A package, at an exact version: `dotnet add <project> package <Id> --version <x.y.z>`
  or `pnpm --dir musichub-ui add <name>@<x.y.z>`. Don't upgrade existing packages.
- A migration: `dotnet ef migrations add <Name> --project musichub-api`. Additive
  only (a new table, a nullable column, an index); never edit an existing migration.
- Check your work: `dotnet build MusicHub.slnx`, `dotnet test MusicHub.slnx`,
  `pnpm --dir musichub-ui install --frozen-lockfile`, then `pnpm --dir musichub-ui lint`
  and `pnpm --dir musichub-ui build`. Never make a check pass by weakening it.
- Commit in small commits; a message says what is now true. Commit no build output.
- Your tools are an explicit list of commands, exactly as written here (one command
  per call, no `cd`, no chaining). Push with the push command the action gave you.

## 4. Deliver

- **Issue**: push the branch and open the PR yourself:
  `gh pr create --base main --title "…" --body "…"`. The body: the user's problem in
  a sentence or two, `Fixes #<issue>`, what changed, **Builds on** (the existing
  capabilities it uses, and why anything new was needed), how you checked it, and
  your assumptions. Link the PR in your comment.
- **PR**: push to its branch. When asked to fix the review, read the reviewers'
  summary and inline comments for the PR's head commit (`gh pr view <PR> --comments`)
  and the build check's log when it failed. Fix every `[bug]`, `[risk]` and `[smell]`
  finding that a code change can fix. A finding that needs a decision: don't work
  around it, say so. When the PR conflicts with main, `git fetch origin main` and
  `git merge origin/main`, then resolve.
- Never merge, approve or close a PR, and never push to `main`. Bas merges.
