import { describe, expect, it } from "vitest"

import { formatFilesFilter, noFilesFilter, parseFilesFilter } from "@/lib/files-filter"

describe("files filter", () => {
  it("round-trips through the hash", () => {
    const filter = { status: "Stored", identification: "Uncertain", page: 3 } as const
    const hash = formatFilesFilter(filter)
    expect(hash).toBe("#/files?status=Stored&identification=Uncertain&page=3")
    expect(parseFilesFilter(hash)).toEqual(filter)
  })

  it("is the plain Files link when nothing is filtered", () => {
    expect(formatFilesFilter(noFilesFilter)).toBe("#/files")
    expect(parseFilesFilter("#/files")).toEqual(noFilesFilter)
  })

  it("ignores unknown values and bad pages", () => {
    expect(
      parseFilesFilter("#/files?status=Nope&identification=Maybe&page=-2")
    ).toEqual(noFilesFilter)
    expect(parseFilesFilter("#/files?page=abc")).toEqual(noFilesFilter)
  })

  it("applies only on the Files screen", () => {
    expect(parseFilesFilter("#/jobs?status=Failed")).toEqual(noFilesFilter)
    expect(parseFilesFilter("#/library?identification=Uncertain")).toEqual(noFilesFilter)
  })

  it("still reads a filter from the earlier Settings link", () => {
    expect(parseFilesFilter("#/settings/files?status=Stored")).toEqual({
      ...noFilesFilter,
      status: "Stored",
    })
  })
})
