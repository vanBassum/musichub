import { Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import type { AudioFilter } from "@/lib/catalog-api"
import { cn } from "@/lib/utils"

interface FilterSidebarProps {
  search: string
  onSearchChange: (search: string) => void
  audio: AudioFilter
  onAudioChange: (audio: AudioFilter) => void
  // Extra classes, e.g. to drop the card chrome inside the mobile sheet.
  className?: string
}

const audioOptions: { value: AudioFilter; label: string }[] = [
  { value: "all", label: "All tracks" },
  { value: "with", label: "With audio" },
  { value: "without", label: "Without audio" },
]

export function FilterSidebar({
  search,
  onSearchChange,
  audio,
  onAudioChange,
  className,
}: FilterSidebarProps) {
  const filtered = search !== "" || audio !== "all"

  return (
    <aside
      aria-label="Filters"
      className={cn(
        "flex min-h-0 flex-col gap-4 rounded-xl border bg-card p-4",
        className
      )}
    >
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Filters</h2>
        <Button
          variant="link"
          size="sm"
          disabled={!filtered}
          onClick={() => {
            onSearchChange("")
            onAudioChange("all")
          }}
          className="px-0"
        >
          Clear all
        </Button>
      </div>
      <div className="relative">
        <Search
          className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          type="search"
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
          placeholder="Search title or artist..."
          aria-label="Search library"
          className="h-9 pl-8"
        />
      </div>
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium">Stored audio</legend>
        {audioOptions.map((option) => (
          <Button
            key={option.value}
            variant={audio === option.value ? "secondary" : "ghost"}
            size="sm"
            aria-pressed={audio === option.value}
            onClick={() => onAudioChange(option.value)}
            className="justify-start"
          >
            {option.label}
          </Button>
        ))}
      </fieldset>
    </aside>
  )
}
