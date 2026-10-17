#!/usr/bin/env bash
# Prints what one Claude Code run used, and the usage status Claude reported, as one
# JSON object. Reads claude-code-action's execution log (its `execution_file`
# output, a JSON array of messages) or `claude -p --output-format stream-json`
# output (one message per line):
#
#   claude-usage.sh <file>
#   {"turns":12,"duration_ms":81234,"cost_usd":0.42,"input":1234,"output":5678,
#    "cache_read":90000,"cache_write":4000,
#    "limit_status":"allowed","limit_type":"five_hour","limit_resets_at":1791895800,
#    "denials":2,"denied":"Bash: mv a b; Bash: rm c"}
#
# denials counts the tool calls the run's permissions refused (an unattended run
# can't approve any), and denied names up to five of them, cut short and with no
# characters that could form markup, so it can be posted as it is. Both are left
# out when nothing was refused.
#
# turns, tokens and cost_usd are the run's own client-side numbers; cost_usd is an
# API-equivalent estimate from a price table, and nothing is billed per token on a
# subscription. limit_* come from the last `rate_limit_event`: Claude's status for
# the subscription's limits (allowed, allowed_warning or rejected), with
# limit_utilization only when it warns, and limit_overage when usage runs on extra
# credit. Neither says how much of the allowance is left.
#
# The formats are not documented, so anything unexpected prints nothing and exits
# non-zero.
set -euo pipefail
[ -s "${1:-}" ] || exit 1
jq -sce '(if length == 1 and (.[0] | type) == "array" then .[0] else . end) as $m
  | ([$m[] | select(type == "object" and .type == "result")] | last) as $r
  | ([$m[] | select(type == "object" and .type == "rate_limit_event")] | last | .rate_limit_info?) as $l
  | ([($r.permission_denials? // [])[]? | select(type == "object")]) as $d
  | {turns: $r.num_turns, duration_ms: $r.duration_ms, cost_usd: $r.total_cost_usd,
     input: $r.usage.input_tokens, output: $r.usage.output_tokens,
     cache_read: $r.usage.cache_read_input_tokens, cache_write: $r.usage.cache_creation_input_tokens,
     limit_status: $l.status, limit_type: $l.rateLimitType, limit_utilization: $l.utilization,
     limit_resets_at: $l.resetsAt, limit_overage: $l.isUsingOverage,
     denials: (if ($d | length) > 0 then ($d | length) else null end),
     denied: (if ($d | length) > 0 then
       [$d[] | ((.tool_name // "?" | tostring)
                + ((.tool_input.command // .tool_input.file_path // null) | if . == null then "" else ": " + tostring end))
             | gsub("[<>`|\\\\\r\n\t]"; " ") | gsub(" +"; " ") | .[0:80]]
       | unique | .[0:5] | join("; ") else null end)}
  | with_entries(select(.value != null and ((.value | type) == "number" or (.value | type) == "string" or (.value | type) == "boolean")))
  | select(length > 0)' "$1"
