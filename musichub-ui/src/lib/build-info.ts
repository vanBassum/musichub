const unknown = "unknown"

const commit = __APP_COMMIT__ || unknown

export const buildInfo = {
  commit,
  shortCommit: commit === unknown ? unknown : commit.slice(0, 7),
  builtAt: __APP_BUILD_TIME__ || unknown,
}
