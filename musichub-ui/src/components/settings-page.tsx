import { FilesView } from "@/components/files-view"
import { JobsView } from "@/components/jobs-view"
import { ProblemsView } from "@/components/problems-view"
import { Button } from "@/components/ui/button"
import { settingsScreens, type SettingsScreenId } from "@/lib/navigation"
import type { FilesState } from "@/lib/use-files"
import type { JobsState } from "@/lib/use-jobs"
import type { ProblemsState } from "@/lib/use-problems"
import { cn } from "@/lib/utils"

export interface SettingsStates {
  filesState: FilesState
  jobsState: JobsState
  problemsState: ProblemsState
  onOpenExecution: (executionId: string) => void
}

// One Settings screen, on the desktop pane and on the phone alike.
export function SettingsScreen({
  id,
  filesState,
  jobsState,
  problemsState,
  onOpenExecution,
  className,
}: SettingsStates & { id: SettingsScreenId; className?: string }) {
  if (id === "files") return <FilesView state={filesState} className={className} />
  if (id === "jobs") return <JobsView state={jobsState} className={className} />
  return (
    <ProblemsView
      state={problemsState}
      onOpenExecution={onOpenExecution}
      className={className}
    />
  )
}

// The desktop's Settings: a list of screens beside the open one.
export function SettingsPage({
  screen,
  onOpenScreen,
  className,
  ...states
}: SettingsStates & {
  screen: SettingsScreenId
  onOpenScreen: (id: SettingsScreenId) => void
  className?: string
}) {
  return (
    <div
      className={cn(
        "grid min-h-0 gap-4 lg:grid-cols-[12rem_minmax(0,1fr)]",
        className
      )}
    >
      <nav
        aria-label="Settings"
        className="flex flex-col gap-1 rounded-xl border bg-card p-2"
      >
        {settingsScreens.map(({ id, label, icon: Icon }) => (
          <Button
            key={id}
            variant={id === screen ? "secondary" : "ghost"}
            aria-current={id === screen ? "page" : undefined}
            className="justify-start"
            onClick={() => onOpenScreen(id)}
          >
            <Icon data-icon="inline-start" />
            {label}
          </Button>
        ))}
      </nav>
      <SettingsScreen id={screen} className="min-h-0" {...states} />
    </div>
  )
}
