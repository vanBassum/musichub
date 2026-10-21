import { EllipsisVertical, ExternalLink } from "lucide-react"
import type { ReactNode } from "react"

import { AvailabilityBadge } from "@/components/availability-badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { availabilityLabels, type Availability } from "@/lib/artist"

interface Source {
  label: string
  url?: string
}

interface ProviderTrackRowProps {
  title: string
  // The one second line: the artist in a search, the version detail on an artist page.
  subtitle?: string
  // Artwork or placeholder before the text.
  leading?: ReactNode
  // Opens the track's page; absent when the row has none.
  onSelect?: () => void
  availability?: Availability
  source: Source
  // The primary actions (add to library, play): always visible and compact.
  actions?: ReactNode
}

const titleClass = "block w-full truncate text-left font-medium"

function SourceLink({ title, source }: { title: string; source: Source }) {
  return (
    <a
      href={source.url}
      target="_blank"
      rel="noreferrer"
      aria-label={`Open ${title} on ${source.label}`}
      className="flex items-center gap-1 hover:text-foreground"
    >
      {source.label}
      <ExternalLink className="size-3" aria-hidden="true" />
    </a>
  )
}

// A track in a list with at most two lines of text: the title, then one line below it.
// The source and availability sit inline from the sm breakpoint up and in the details
// menu on a phone, so the title never competes with them for width.
export function ProviderTrackRow({
  title,
  subtitle,
  leading,
  onSelect,
  availability,
  source,
  actions,
}: ProviderTrackRowProps) {
  return (
    <li className="flex items-center gap-2 px-2 py-1.5 text-sm">
      {leading}
      <div className="min-w-0 flex-1">
        {onSelect ? (
          <button
            type="button"
            className={`${titleClass} hover:underline`}
            title={title}
            onClick={onSelect}
          >
            {title}
          </button>
        ) : (
          <div className={titleClass} title={title}>
            {title}
          </div>
        )}
        {subtitle && (
          <div
            className="truncate text-xs text-muted-foreground"
            title={subtitle}
          >
            {subtitle}
          </div>
        )}
      </div>
      <div className="hidden shrink-0 items-center gap-2 text-xs text-muted-foreground sm:flex">
        {source.url ? (
          <SourceLink title={title} source={source} />
        ) : (
          <span>{source.label}</span>
        )}
        {availability && <AvailabilityBadge availability={availability} />}
      </div>
      <div className="flex shrink-0 items-center gap-1">
        {actions}
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button
                variant="ghost"
                size="icon"
                className="size-9 sm:hidden"
                aria-label={`Details for ${title}`}
              />
            }
          >
            <EllipsisVertical />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem disabled>Source: {source.label}</DropdownMenuItem>
            {availability && (
              <DropdownMenuItem disabled>
                {availabilityLabels[availability]}
              </DropdownMenuItem>
            )}
            {source.url && (
              <DropdownMenuItem
                render={
                  <a
                    href={source.url}
                    target="_blank"
                    rel="noreferrer"
                    aria-label={`Open ${title} on ${source.label}`}
                  />
                }
              >
                Open on {source.label}
                <ExternalLink aria-hidden="true" />
              </DropdownMenuItem>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </li>
  )
}
