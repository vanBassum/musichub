import { useMemo } from "react"

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
  search: "",
  page: 1,
}

export const isFilesFiltered = ({ status, identification, search }: FilesQuery) =>
  status !== null || identification !== null || search !== ""

// The Files filter is in the hash, so a filtered list can be linked to and reloaded:
// `#/files?status=Stored&identification=Uncertain&search=live&page=2`. Unknown values are ignored.
export function parseFilesFilter(hash: string): FilesQuery {
  if (parseLocation(hash).tab !== "files") return noFilesFilter
  const params = new URLSearchParams(hash.split("?")[1] ?? "")
  const status = fileStatuses.find((s) => s === params.get("status")) ?? null
  const identification =
    identificationFilters.find((s) => s === params.get("identification")) ?? null
  const page = Number(params.get("page"))
  return {
    status,
    identification,
    search: (params.get("search") ?? "").trim(),
    page: Number.isInteger(page) && page > 1 ? page : 1,
  }
}

export function formatFilesFilter({ status, identification, search, page }: FilesQuery) {
  const params = new URLSearchParams()
  if (status) params.set("status", status)
  if (identification) params.set("identification", identification)
  if (search) params.set("search", search)
  if (page > 1) params.set("page", String(page))
  const query = params.toString()
  return `#/files${query ? `?${query}` : ""}`
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
