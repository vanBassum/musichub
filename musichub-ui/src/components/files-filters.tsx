import { ChevronLeft, ChevronRight, Search } from "lucide-react"
import { useEffect, useState } from "react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  fileStatuses,
  FILES_PAGE_SIZE,
  identificationFilters,
  identificationStates,
  type FileCounts,
  type FileIdentificationState,
  type FilesPage,
  type FilesQuery,
  type FileStatus,
} from "@/lib/files-api"
import { isFilesFiltered, noFilesFilter } from "@/lib/files-filter"

// States that rarely occur for a stored file: offered only while some file is in them, or when chosen.
const rareStates = new Set(["NoMethodRan", "Mismatch"])

const SEARCH_DELAY_MS = 300

const ALL = "all"

// One dropdown filter: "All …" clears it; every other option shows its count.
function FilterSelect({
  label,
  allLabel,
  value,
  options,
  onChange,
}: {
  label: string
  allLabel: string
  value: string | null
  options: { key: string; label: string; hint?: string; count: number }[]
  onChange: (value: string | null) => void
}) {
  const items = [
    { value: ALL, label: allLabel },
    ...options.map((o) => ({ value: o.key, label: `${o.label} (${o.count})` })),
  ]
  return (
    <Select
      value={value ?? ALL}
      items={items}
      onValueChange={(next) => onChange(next === null || next === ALL ? null : next)}
    >
      <SelectTrigger aria-label={label} className="min-w-0 flex-1 sm:flex-none">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={ALL}>{allLabel}</SelectItem>
        {options.map((option) => (
          <SelectItem key={option.key} value={option.key} title={option.hint}>
            {option.label} ({option.count})
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

// The search box keeps what is typed and applies it once typing pauses; a search changed elsewhere
// (Clear filters, the back button) replaces what is typed.
function FilesSearch({ filter, onChange }: { filter: FilesQuery; onChange: (filter: FilesQuery) => void }) {
  const [draft, setDraft] = useState(filter.search)
  const [applied, setApplied] = useState(filter.search)
  if (applied !== filter.search) {
    setApplied(filter.search)
    setDraft(filter.search)
  }

  useEffect(() => {
    const typed = draft.trim()
    if (typed === filter.search) return
    const timer = setTimeout(
      () => onChange({ ...filter, search: typed, page: 1 }),
      SEARCH_DELAY_MS
    )
    return () => clearTimeout(timer)
  }, [draft, filter, onChange])

  return (
    <div className="relative min-w-48 flex-1 sm:max-w-sm">
      <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
      <Input
        type="search"
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
        placeholder="Search files…"
        aria-label="Search files"
        className="h-8 pl-7"
      />
    </div>
  )
}

// One compact row: filename search, a Status dropdown and an Identification dropdown, combined. Counts follow the
// search. Any change starts again at the first page.
export function FilesFilters({
  filter,
  counts,
  onChange,
}: {
  filter: FilesQuery
  counts: FileCounts | null
  onChange: (filter: FilesQuery) => void
}) {
  const identification = identificationFilters
    .filter(
      (state) =>
        !rareStates.has(state) ||
        filter.identification === state ||
        (counts?.identification[state] ?? 0) > 0
    )
    .map((state) => ({
      key: state,
      label: identificationStates[state].label,
      hint: identificationStates[state].hint,
      count: counts?.identification[state] ?? 0,
    }))
  const statuses = fileStatuses.map((status) => ({
    key: status,
    label: status,
    count: counts?.statuses[status] ?? 0,
  }))

  return (
    <div className="flex shrink-0 flex-wrap items-center gap-2 border-t px-4 py-2 text-sm">
      <FilesSearch filter={filter} onChange={onChange} />
      <FilterSelect
        label="Status"
        allLabel="All statuses"
        value={filter.status}
        options={statuses}
        onChange={(status) =>
          onChange({ ...filter, page: 1, status: status as FileStatus | null })
        }
      />
      <FilterSelect
        label="Identification"
        allLabel="All identification states"
        value={filter.identification}
        options={identification}
        onChange={(state) =>
          onChange({
            ...filter,
            page: 1,
            identification: state as FileIdentificationState | null,
          })
        }
      />
    </div>
  )
}

// How many files match the active filters (all of them, not only this page), and the page controls.
export function FilesResultBar({
  filter,
  page,
  onChange,
}: {
  filter: FilesQuery
  page: FilesPage | null
  onChange: (filter: FilesQuery) => void
}) {
  if (!page) return null
  const filtered = isFilesFiltered(filter)
  const pages = Math.max(1, Math.ceil(page.total / FILES_PAGE_SIZE))
  const first = page.items.length === 0 ? 0 : (page.page - 1) * FILES_PAGE_SIZE + 1
  const last = (page.page - 1) * FILES_PAGE_SIZE + page.items.length
  const goTo = (next: number) => onChange({ ...filter, page: next })

  return (
    <div className="flex shrink-0 flex-wrap items-center gap-2 border-t px-4 py-2 text-sm">
      <span role="status">
        {page.total} {page.total === 1 ? "file matches" : "files match"}
        {filtered ? " the filters" : ""}
        {page.total > 0 && pages > 1 && ` · showing ${first}–${last}`}
      </span>
      {filtered && (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => onChange(noFilesFilter)}
        >
          Clear filters
        </Button>
      )}
      {(pages > 1 || page.page > 1) && (
        <nav aria-label="Pages" className="ml-auto flex items-center gap-1.5">
          <Button
            variant="outline"
            size="sm"
            disabled={page.page <= 1}
            onClick={() => goTo(page.page - 1)}
          >
            <ChevronLeft data-icon="inline-start" />
            Previous
          </Button>
          <span aria-current="page">
            Page {page.page} of {pages}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={page.page >= pages}
            onClick={() => goTo(page.page + 1)}
          >
            Next
            <ChevronRight data-icon="inline-end" />
          </Button>
        </nav>
      )}
    </div>
  )
}
