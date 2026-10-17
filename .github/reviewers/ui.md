# Focus: the UI, its components and its correctness

You check the changes under `musichub-ui/`. The API under `musichub-api/` is the api
reviewer's, except for how the UI calls it.

Background: Vite + React 19 + TypeScript, Tailwind 4 and shadcn/ui on Base UI
(`components.json`), pnpm. Generated shadcn/ui components live in
`src/components/ui/`, shared helpers in `src/lib/` (`cn` in `lib/utils.ts`), the
theme (light, dark, system) in `src/components/theme-provider.tsx`. Imports use the
`@/` alias for `src/`. In development Vite proxies `/api` to the API; in the image
the API serves the built UI, so the UI calls the API with relative `/api/...` paths.

Look for:

- **Forked markup instead of a shared component.** Hand-written buttons, inputs,
  dialogs, menus, cards, tables or other controls where a component in
  `src/components/ui/` already exists. If the component can't do what the PR needs,
  the fix is to extend or wrap it, not to copy its markup. Reusable app-specific
  components outside `components/ui/` are fine.
- **New shadcn/ui components.** A feature PR may add the official shadcn/ui
  components it uses, in the same PR, generated with the shadcn CLI and committed
  as generated (formatting aside). Check that each one is used, and that the PR
  doesn't hand-write a substitute for an official component (`[risk]` when a PR
  hand-writes one into `components/ui/`). A new or upgraded npm package
  (`package.json`, `pnpm-lock.yaml`) is a separate decision: `[risk]` unless the PR
  says Bas approved it.
- **Edits to generated components.** Changes inside `src/components/ui/` that
  change behaviour for every caller, without the PR saying so.
- **Styling.** Tailwind utilities and the theme tokens from `src/index.css`.
  Hard-coded colours that break dark mode, inline styles that duplicate a utility,
  and new custom CSS where a utility exists are a problem. Class merging goes
  through `cn`.
- **Logic bugs.** Off-by-one errors, wrong conditions, `null`/`undefined` access on
  optional API fields, unhandled edge cases (no data, one item, an empty selection),
  and date/time or time-zone mistakes.
- **React state.** Missing or wrong `useEffect`/`useMemo`/`useCallback`
  dependencies that show stale data, effects without cleanup (listeners, timers,
  aborting a fetch), state derived from props or other state but stored separately
  so it can drift, unstable or index keys in lists that reorder, and state updates
  after unmount.
- **Calling the API.** Absolute URLs or a hard-coded host or port instead of
  `/api/...`, responses used without checking `response.ok`, errors that leave the
  UI in a loading state forever, and a second ad-hoc fetch helper beside an
  existing one.
- **Types.** `any`, non-null assertions (`!`) or `@ts-ignore` / `@ts-expect-error`
  that hide a real mismatch with what the API returns.
- **Accessibility.** Interactive elements that aren't buttons or links, icon-only
  buttons without an accessible name, and form inputs without a label.
- **Performance.** Work that runs on every render and scales with the data, such as
  rebuilding large arrays or sorting in render without `useMemo`.
