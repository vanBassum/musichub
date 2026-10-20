# Code quality

The smells reviewer (`.github/reviewers/smells.md`) most often sends work back for
these three. Before you commit, read your own diff once (`git diff`) for them and
fix what you find.

- **No duplicate logic or repeated UI blocks.** The same block, markup or check
  written twice or more becomes one component or helper, used everywhere.
- **Keep components and functions easy to follow.** Split by responsibility, not by
  line count: a part that loads, one that decides, one that renders. Return early
  instead of nesting, and don't let a later branch override an earlier one.
- **One owner per decision.** A rule used in several places (a check, a parse, a
  URL or state decision) lives in one named helper or hook, and everything calls
  that.
