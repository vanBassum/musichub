// PreToolUse guard for Claude sessions in this repo, on every machine (see the
// "Git workflow" section of CLAUDE.md):
// - never push to main, and never skip the git hooks that refuse it;
// - musichub-understanding is read-only: no file edits and no writing shell or
//   GitHub commands against it. When it needs to change, Claude tells Bas instead.
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

  if (PUSH_TO_MAIN.test(cmd))
    return deny("Never push to main in musichub. Push a branch and open a PR to main (CLAUDE.md, Git workflow).")
  if (SKIP_HOOKS.test(cmd))
    return deny("Don't skip or redirect the git hooks in musichub: .githooks/pre-push is what keeps pushes off main.")

  const understandingReason =
    "musichub-understanding is read-only for Claude. Never write to it (no files, commits, branches, pushes or PRs). " +
    "If the understanding needs to change, stop and tell Bas what and why; he changes it or redirects you."
  if (/^(Edit|Write|MultiEdit|NotebookEdit)$/.test(tool) && UNDERSTANDING.test(file))
    return deny(understandingReason)
  if ((UNDERSTANDING.test(cmd) || UNDERSTANDING.test(cwd)) && WRITES.some((re) => re.test(cmd)))
    return deny(understandingReason)
})
