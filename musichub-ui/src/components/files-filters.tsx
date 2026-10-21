import { ChevronLeft, ChevronRight } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  fileStatuses,
  FILES_PAGE_SIZE,
  identificationFilters,
  identificationStates,
  type FileCounts,
  type FilesPage,
  type FilesQuery,
} from "@/lib/files-api"
import { isFilesFiltered, noFilesFilter } from "@/lib/files-filter"

// States that rarely occur for a stored file: offered only while some file is in them, or when chosen.
const rareStates = new Set(["NoMethodRan", "Mismatch"])

function FilterGroup({
  label,
  options,
}: {
  label: string
  options: { key: string; label: string; hint?: string; count: number; active: boolean; onToggle: () => void }[]
}) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap items-center gap-1.5">
      <span className="text-muted-foreground">{label}</span>
      {options.map((option) => (
        <Button
          key={option.key}
          variant={option.active ? "secondary" : "outline"}
          size="sm"
          aria-pressed={option.active}
          title={option.hint}
          onClick={option.onToggle}
        >
          {option.label} ({option.count})
        </Button>
      ))}
    </div>
  )
}

// Filters the list by status and identification state, with the count of each. Choosing the active one clears it;
// any change starts again at the first page.
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
      active: filter.identification === state,
      onToggle: () =>
        onChange({
          ...filter,
          page: 1,
          identification: filter.identification === state ? null : state,
        }),
    }))
  const statuses = fileStatuses.map((status) => ({
    key: status,
    label: status,
    count: counts?.statuses[status] ?? 0,
    active: filter.status === status,
    onToggle: () =>
      onChange({
        ...filter,
        page: 1,
        status: filter.status === status ? null : status,
      }),
  }))

  return (
    <div className="flex shrink-0 flex-col gap-2 border-t px-4 py-2 text-sm">
      <FilterGroup label="Status" options={statuses} />
      <FilterGroup label="Identification" options={identification} />
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
