// Tests for guard.js: feeds it PreToolUse inputs and checks allow/deny.
// Run: node .claude/hooks/guard.test.js
const { execFileSync } = require("child_process")
const path = require("path")

const GUARD = path.join(__dirname, "guard.js")

function decide(tool_name, tool_input, cwd = "C:/Workspace/musichub") {
  const out = execFileSync(process.execPath, [GUARD], { input: JSON.stringify({ tool_name, tool_input, cwd }) }).toString()
  return out ? JSON.parse(out).hookSpecificOutput.permissionDecision : "allow"
}
const sh = (command, cwd) => decide("Bash", { command }, cwd)

const cases = [
  ["push to main", sh("git push origin main"), "deny"],
  ["push HEAD to main", sh("git push origin HEAD:main"), "deny"],
  ["force-push to main", sh("git push -f origin +main"), "deny"],
  ["push to refs/heads/main", sh("git push origin HEAD:refs/heads/main"), "deny"],
  ["push to main, chained", sh("git status && git push origin main && echo ok"), "deny"],
  ["push with --no-verify", sh("git push --no-verify origin feature/x"), "deny"],
  ["commit with -n", sh("git commit -n -m wip"), "deny"],
  ["unset hooksPath", sh("git config --unset core.hooksPath"), "deny"],
  ["redirect hooksPath", sh("git -c core.hooksPath=/dev/null push origin x"), "deny"],
  ["edit understanding file", decide("Edit", { file_path: "D:/src/musichub-understanding/features/features.md" }), "deny"],
  ["write understanding file", decide("Write", { file_path: "/home/x/musichub-understanding/logs/a.md" }), "deny"],
  ["commit in understanding", sh("git -C ../musichub-understanding commit -m x"), "deny"],
  ["shell write from understanding cwd", sh("echo x > notes.md", "C:/Workspace/musichub-understanding"), "deny"],
  ["gh write to understanding", sh("gh pr create -R vanBassum/musichub-understanding"), "deny"],

  ["push a feature branch", sh("git push -u origin feature/main-menu"), "allow"],
  ["push a branch named main-fix", sh("git push origin fix/main-fix"), "allow"],
  ["push a tag", sh("git push origin v0.1.0"), "allow"],
  ["switch to main and pull", sh("git switch main && git pull"), "allow"],
  ["enable the repo hooks", sh("git config core.hooksPath .githooks"), "allow"],
  ["read understanding", sh("git -C ../musichub-understanding log --oneline -3"), "allow"],
  ["edit a repo file", decide("Edit", { file_path: "C:/Workspace/musichub/CLAUDE.md" }), "allow"],
  ["open a PR", sh("gh pr create --base main --title x"), "allow"],
]

let failed = 0
for (const [name, got, want] of cases) {
  const ok = got === want
  if (!ok) failed++
  console.log(`${ok ? "ok  " : "FAIL"}  ${name}${ok ? "" : ` (got ${got}, want ${want})`}`)
}
console.log(`${cases.length - failed} passed, ${failed} failed`)
process.exitCode = failed ? 1 : 0
