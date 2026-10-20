# Code quality

The smells reviewer (`.github/reviewers/smells.md`) most often sends work back for
duplicating code that already exists elsewhere in the repository, which a look at
your own diff doesn't show. So reuse first, then check your diff.

- **Reuse before you write.** Before adding a helper, hook, mapping, query or
  check, grep for an existing one: the same condition, the same field mapping, the
  same load/refresh pattern, the same path or URL rule. Use it, or move it to a
  shared place and use it from both. Never copy it.
- **No duplicate logic or repeated UI blocks.** The same block, markup or check
  written twice or more becomes one component or helper, used everywhere.
- **Keep components and functions easy to follow.** Split by responsibility, not by
  line count: a part that loads, one that decides, one that renders. Return early
  instead of nesting, and don't let a later branch override an earlier one.
- **One owner per decision.** A rule used in several places (a check, a parse, a
  URL or state decision) lives in one named helper or hook, and everything calls
  that.

Before you commit, read your own diff once (`git diff`) against these rules and fix
what you find.
