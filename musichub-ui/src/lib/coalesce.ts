// Runs `run` at once on the first call, then at most once per `windowMs`: calls made
// inside the window collapse into one trailing run. A burst of upload completions
// therefore costs one list request instead of one each.
export function coalesce(run: () => void, windowMs: number) {
  let timer: ReturnType<typeof setTimeout> | null = null
  let trailing = false

  const open = () => {
    timer = setTimeout(() => {
      timer = null
      if (!trailing) return
      trailing = false
      run()
      open()
    }, windowMs)
  }

  const call = () => {
    if (timer !== null) {
      trailing = true
      return
    }
    run()
    open()
  }

  call.cancel = () => {
    if (timer !== null) clearTimeout(timer)
    timer = null
    trailing = false
  }
  return call
}
