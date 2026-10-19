import { useState } from "react"

import { mockNowPlayingId, mockQueue, type Track } from "@/data/mock-tracks"

export interface PlayerState {
  queue: Track[]
  nowPlayingId: string
  current: Track | null
  playing: boolean
  shuffle: boolean
  repeat: boolean
  onTogglePlaying: () => void
  onToggleShuffle: () => void
  onToggleRepeat: () => void
}

// The one placeholder player state, held by the app shell and shared by every layout.
// Nothing plays: the toggles only show how the controls look in each state.
export function usePlayer(): PlayerState {
  const [playing, setPlaying] = useState(false)
  const [shuffle, setShuffle] = useState(false)
  const [repeat, setRepeat] = useState(false)

  return {
    queue: mockQueue,
    nowPlayingId: mockNowPlayingId,
    current: mockQueue.find((track) => track.id === mockNowPlayingId) ?? null,
    playing,
    shuffle,
    repeat,
    onTogglePlaying: () => setPlaying((value) => !value),
    onToggleShuffle: () => setShuffle((value) => !value),
    onToggleRepeat: () => setRepeat((value) => !value),
  }
}
