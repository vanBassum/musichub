import { Search, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"

interface LibrarySearchProps {
  value: string
  onChange: (value: string) => void
}

// Searches the user's own catalog only (the Library's server-side search), apart from
// the external Search page.
export function LibrarySearch({ value, onChange }: LibrarySearchProps) {
  return (
    <div className="px-4 pb-3">
      <div className="relative">
        <Search
          className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          type="search"
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder="Search title or artist..."
          aria-label="Search library"
          className="h-8 pr-8 pl-8 [&::-webkit-search-cancel-button]:hidden"
        />
        {value !== "" && (
          <Button
            variant="ghost"
            size="icon-xs"
            aria-label="Clear library search"
            onClick={() => onChange("")}
            className="absolute top-1/2 right-1.5 -translate-y-1/2"
          >
            <X />
          </Button>
        )}
      </div>
    </div>
  )
}
