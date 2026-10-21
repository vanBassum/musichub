// PreToolUse guard for Claude sessions in this repo, on every machine (see the
// "Git workflow" section of CLAUDE.md):
// - in this repo, never push to main, and never skip the git hooks that refuse
//   it. Other repositories a session works in (strato-stack, say) have their
//   own rules; these two are about musichub's main;
// - musichub-understanding is read-only, from any repo: no file edits and no
//   writing shell or GitHub commands against it. When it needs to change,
//   Claude tells Bas instead.
const path = require("path")

const UNDERSTANDING = /musichub-understanding/i

const PUSH_TO_MAIN = /\bgit\b[^|;&\n]*\bpush\b[^|;&\n]*(?:[\s:+]|refs\/heads\/)main(?=$|[\s;&|])/i
const SKIP_HOOKS = /\bgit\b[^|;&\n]*\b(push|commit)\b[^|;&\n]*--no-verify|\bgit\b[^|;&\n]*\bcommit\b[^|;&\n]*\s-n\b|\bcore\.hooksPath\b(?!\s+\.githooks\b)/i

const WRITES = [
  // git that changes the repo, its refs or its remote
  /\bgit\b[^|;&\n]*\b(commit|push|add|rm|mv|rebase|merge|cherry-pick|revert|tag|reset|stash|apply|am|restore|clean|init|config|remote\s+(add|set-url|remove))\b/i,
  /\bgit\b[^|;&\n]*\b(checkout\s+-b|switch\s+-c|branch\s+-[dDmMcC])\b/i,
  // GitHub writes
  /\bgh\s+(pr\s+(create|merge|edit|close|reopen|ready|review|comment)|issue\s+(create|edit|comment|close|reopen)|repo\s+(edit|delete|rename|archive)|release\s+(create|edit|delete)|workflow\s+(run|enable|disable)|run\s+(rerun|cancel)|label\s+(create|edit|delete)|secret|variable)\b/i,
  /\bgh\s+api\b.*(\s-X\s*|--method[\s=]*)(POST|PUT|PATCH|DELETE)\b/i,
  /\bgh\s+api\b.*\s-(f|F|-field|-raw-field|-input)\b/,
  // file writes
  /\b(Out-File|Set-Content|Add-Content|New-Item|Remove-Item|Move-Item|Copy-Item|Rename-Item|Clear-Content)\b/i,
  /\[IO\.File\]::(Write|Append|Delete|Move|Copy)/i,
  /\b(sed\s+-i|rm|mv|cp|touch|mkdir|rmdir|tee|truncate)\b/,
  /(^|[^2&])>{1,2}(?!\s*(\/dev\/null|\$null|&))/,
]

// This repository: the project Claude runs in, else the checkout this file is in.
const REPO = norm(process.env.CLAUDE_PROJECT_DIR || path.resolve(__dirname, "..", ".."))

// One spelling for a path: forward slashes, lowercase, no trailing slash, and
// Git Bash's /c/... as c:/... .
function norm(p) {
  let n = String(p).trim().replace(/^["']|["']$/g, "").replace(/\\/g, "/")
  const drive = /^\/([a-z])(\/|$)/i.exec(n)
  if (drive) n = drive[1] + ":/" + n.slice(3)
  return n.replace(/\/+$/, "").toLowerCase()
}

function resolveFrom(base, p) {
  const n = norm(p)
  return /^[a-z]:\//.test(n) || n.startsWith("/") || n.startsWith("~") ? n : norm(path.posix.normalize(`${base}/${n}`))
}

const inRepo = (dir) => dir === REPO || dir.startsWith(`${REPO}/`)

// Whether a git command matching `re` runs in this repository. Each command of
// a chain runs in its own -C directory, else in the last cd before it, else in
// the session's cwd.
function runsHere(cmd, cwd, re) {
  let dir = norm(cwd || REPO)
  for (const part of cmd.split(/&&|\|\||[;&|\n]/)) {
    const cd = /^\s*(?:cd|pushd|Set-Location|sl)\s+(?:-(?:Literal)?Path\s+)?("[^"]+"|'[^']+'|\S+)/i.exec(part)
    if (cd) { dir = resolveFrom(dir, cd[1]); continue }
    if (!re.test(part)) continue
    const c = /\bgit\s+(?:-c\s+\S+\s+)*-C\s+("[^"]+"|'[^']+'|\S+)/i.exec(part)
    if (inRepo(c ? resolveFrom(dir, c[1]) : dir)) return true
  }
  return false
}

function deny(reason) {
  // No process.exit(): on Windows it can drop piped stdout before it is flushed.
  process.stdout.write(JSON.stringify({
    hookSpecificOutput: { hookEventName: "PreToolUse", permissionDecision: "deny", permissionDecisionReason: reason },
  }))
}

let raw = ""
process.stdin.on("data", (c) => (raw += c))
process.stdin.on("end", () => {
  let input
  try { input = JSON.parse(raw) } catch { return }
  const tool = String(input?.tool_name ?? "")
  const cmd = String(input?.tool_input?.command ?? "")
  const file = String(input?.tool_input?.file_path ?? input?.tool_input?.notebook_path ?? "")
  const cwd = String(input?.cwd ?? "")

  if (runsHere(cmd, cwd, PUSH_TO_MAIN))
    return deny("Never push to main in musichub. Push a branch and open a PR to main (CLAUDE.md, Git workflow).")
  if (runsHere(cmd, cwd, SKIP_HOOKS))
    return deny("Don't skip or redirect the git hooks in musichub: .githooks/pre-push is what keeps pushes off main.")

  const understandingReason =
    "musichub-understanding is read-only for Claude. Never write to it (no files, commits, branches, pushes or PRs). " +
    "If the understanding needs to change, stop and tell Bas what and why; he changes it or redirects you."
  if (/^(Edit|Write|MultiEdit|NotebookEdit)$/.test(tool) && UNDERSTANDING.test(file))
    return deny(understandingReason)
  if ((UNDERSTANDING.test(cmd) || UNDERSTANDING.test(cwd)) && WRITES.some((re) => re.test(cmd)))
    return deny(understandingReason)
})
