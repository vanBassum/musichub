// vite.config.ts already turns unset or empty build values into "unknown".
const commit = __APP_COMMIT__

export const buildInfo = {
  commit,
  shortCommit: commit === "unknown" ? commit : commit.slice(0, 7),
  builtAt: __APP_BUILD_TIME__,
}
