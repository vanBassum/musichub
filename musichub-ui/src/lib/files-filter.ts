import { useMemo } from "react"

import { formatSettingsHash } from "@/components/settings-sections"
import {
  fileStatuses,
  identificationFilters,
  type FilesQuery,
} from "@/lib/files-api"
import { parseLocation } from "@/lib/navigation"
import { useHash } from "@/lib/use-location"

export const noFilesFilter: FilesQuery = {
  status: null,
  identification: null,
  page: 1,
}

export const isFilesFiltered = ({ status, identification }: FilesQuery) =>
  status !== null || identification !== null

// The Files filter is in the hash, so a filtered list can be linked to and reloaded:
// `#/settings/files?status=Stored&identification=Uncertain&page=2`. Unknown values are ignored.
export function parseFilesFilter(hash: string): FilesQuery {
  const location = parseLocation(hash)
  if (location.tab !== "settings" || location.screen !== "files")
    return noFilesFilter
  const params = new URLSearchParams(hash.split("?")[1] ?? "")
  const status = fileStatuses.find((s) => s === params.get("status")) ?? null
  const identification =
    identificationFilters.find((s) => s === params.get("identification")) ?? null
  const page = Number(params.get("page"))
  return {
    status,
    identification,
    page: Number.isInteger(page) && page > 1 ? page : 1,
  }
}

export function formatFilesFilter({ status, identification, page }: FilesQuery) {
  const params = new URLSearchParams()
  if (status) params.set("status", status)
  if (identification) params.set("identification", identification)
  if (page > 1) params.set("page", String(page))
  const query = params.toString()
  return `${formatSettingsHash("files")}${query ? `?${query}` : ""}`
}

// The current Files filter, read from the URL hash, and the setter that writes it back (a history entry).
export function useFilesFilter() {
  const hash = useHash()
  const filter = useMemo(() => parseFilesFilter(hash), [hash])
  return {
    filter,
    setFilter: (next: FilesQuery) => {
      window.location.hash = formatFilesFilter(next)
    },
  }
}
