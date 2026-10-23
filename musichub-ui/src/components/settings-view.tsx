import { BuildInfoList } from "@/components/build-info-sheet"
import { cn } from "@/lib/utils"

// Settings holds configuration. Until there is something to configure, it shows the
// build this page is served from.
export function SettingsView({ className }: { className?: string }) {
  return (
    <section
      aria-labelledby="settings-heading"
      className={cn(
        "flex min-h-0 min-w-0 flex-col gap-4 overflow-auto rounded-xl border bg-card p-4",
        className
      )}
    >
      <h1 id="settings-heading" className="text-xl font-semibold">
        Settings
      </h1>
      <h2 className="text-sm font-medium text-muted-foreground">Build</h2>
      <BuildInfoList />
    </section>
  )
}
