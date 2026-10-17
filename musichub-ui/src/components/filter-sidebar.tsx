import { ChevronDown, Search } from "lucide-react"
import type { ReactNode } from "react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Input } from "@/components/ui/input"
import { Slider } from "@/components/ui/slider"
import { trackStatuses, type Track } from "@/data/mock-tracks"

interface FilterSidebarProps {
  tracks: Track[]
}

interface FilterGroupProps {
  title: string
  defaultOpen?: boolean
  children?: ReactNode
}

// Groups open and close; the filters inside are placeholders until filtering exists.
function FilterGroup({
  title,
  defaultOpen = false,
  children,
}: FilterGroupProps) {
  return (
    <Collapsible defaultOpen={defaultOpen} className="border-b last:border-b-0">
      <CollapsibleTrigger className="group flex w-full items-center justify-between py-2.5 text-sm font-medium outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/30">
        {title}
        <ChevronDown
          className="size-4 text-muted-foreground transition-transform group-data-[panel-open]:rotate-180"
          aria-hidden="true"
        />
      </CollapsibleTrigger>
      <CollapsibleContent className="pb-3">
        {children ?? (
          <p className="text-xs text-muted-foreground">Not available yet.</p>
        )}
      </CollapsibleContent>
    </Collapsible>
  )
}

export function FilterSidebar({ tracks }: FilterSidebarProps) {
  const counts = trackStatuses.map((status) => ({
    ...status,
    count: tracks.filter((track) => track.status === status.value).length,
  }))

  return (
    <aside
      aria-label="Filters"
      className="flex min-h-0 flex-col gap-4 rounded-xl border bg-card p-4"
    >
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Filters</h2>
        <Button
          variant="link"
          size="sm"
          disabled
          title="Not available yet"
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
          disabled
          placeholder="Search..."
          aria-label="Search library"
          className="h-9 pl-8"
        />
      </div>
      <div className="-mx-1 flex min-h-0 flex-col overflow-y-auto px-1">
        <FilterGroup title="Artist" />
        <FilterGroup title="Album" />
        <FilterGroup title="Genre" />
        <FilterGroup title="Year" />
        <FilterGroup title="Status" defaultOpen>
          <ul className="flex flex-col gap-2.5">
            <li className="flex items-center gap-3 text-sm">
              <Checkbox disabled aria-label="All" />
              <span className="flex-1 opacity-50">All</span>
              <span className="text-xs text-muted-foreground">
                {tracks.length}
              </span>
            </li>
            {counts.map((status) => (
              <li
                key={status.value}
                className="flex items-center gap-3 text-sm"
              >
                <Checkbox disabled aria-label={status.label} />
                <span className="flex-1 opacity-50">{status.label}</span>
                <span className="text-xs text-muted-foreground">
                  {status.count}
                </span>
              </li>
            ))}
          </ul>
        </FilterGroup>
        <FilterGroup title="Quality" />
        <FilterGroup title="File size" />
        <FilterGroup title="Duration" defaultOpen>
          <div className="flex flex-col gap-2 px-1 pt-1">
            <Slider
              disabled
              defaultValue={[0, 15]}
              min={0}
              max={15}
              aria-label="Duration in minutes"
            />
            <div className="flex justify-between text-xs text-muted-foreground">
              <span>0:00</span>
              <span>15:00</span>
            </div>
          </div>
        </FilterGroup>
        <FilterGroup title="Source" />
      </div>
    </aside>
  )
}
