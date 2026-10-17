const filters = ["Artist", "Genre", "Year", "Format"]

export function FilterSidebar() {
  return (
    <aside
      aria-label="Filters"
      className="flex flex-col gap-4 border-b p-4 md:w-56 md:shrink-0 md:border-r md:border-b-0"
    >
      <div className="flex items-center justify-between">
        <h2 className="text-xs font-medium">Filters</h2>
        <span className="text-[0.625rem] text-muted-foreground uppercase">
          Placeholder
        </span>
      </div>
      <input
        type="search"
        disabled
        placeholder="Search library"
        aria-label="Search library"
        className="h-7 w-full rounded-md border bg-input/30 px-2 text-xs disabled:cursor-not-allowed disabled:opacity-50"
      />
      {filters.map((filter) => (
        <label key={filter} className="flex flex-col gap-1 text-xs">
          <span className="text-muted-foreground">{filter}</span>
          <select
            disabled
            className="h-7 w-full rounded-md border bg-input/30 px-2 text-xs disabled:cursor-not-allowed disabled:opacity-50"
          >
            <option>All</option>
          </select>
        </label>
      ))}
    </aside>
  )
}
