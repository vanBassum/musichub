import { FilesView } from "@/components/files-view"
import { JobsView } from "@/components/jobs-view"
import {
  settingsSections,
  type SettingsSectionId,
} from "@/components/settings-sections"
import { Button } from "@/components/ui/button"
import type { FilesState } from "@/lib/use-files"
import type { JobsState } from "@/lib/use-jobs"
import { cn } from "@/lib/utils"

// The state Settings' sections show, held by the App so it survives navigation.
export interface SettingsStates {
  filesState: FilesState
  jobsState: JobsState
}

interface SettingsViewProps extends SettingsStates {
  section: SettingsSectionId
  onSectionChange: (section: SettingsSectionId) => void
  className?: string
}

// Settings groups the management screens: a section list beside the section on desktop,
// a row of sections above it on a phone. Each section keeps its own view and state.
export function SettingsView({
  section,
  onSectionChange,
  filesState,
  jobsState,
  className,
}: SettingsViewProps) {
  return (
    <div
      className={cn(
        "flex min-h-0 min-w-0 flex-col gap-3 lg:flex-row lg:gap-4",
        className
      )}
    >
      <nav
        aria-label="Settings"
        className="grid shrink-0 grid-cols-2 gap-1 lg:flex lg:w-64 lg:flex-col lg:gap-1 lg:self-start xl:w-72"
      >
        {settingsSections.map(({ id, label, description, icon: Icon }) => {
          const active = id === section
          return (
            <Button
              key={id}
              variant={active ? "secondary" : "ghost"}
              aria-current={active ? "page" : undefined}
              onClick={() => onSectionChange(id)}
              className={cn(
                "h-auto justify-center gap-2 py-2 lg:justify-start lg:py-3",
                active && "bg-primary/15 text-primary hover:bg-primary/20"
              )}
            >
              <Icon />
              <span className="flex min-w-0 flex-col items-start text-left">
                {label}
                <span className="hidden text-xs font-normal whitespace-normal text-muted-foreground lg:block">
                  {description}
                </span>
              </span>
            </Button>
          )
        })}
      </nav>
      {section === "files" && (
        <FilesView state={filesState} className="min-h-0 flex-1" />
      )}
      {section === "jobs" && (
        <JobsView state={jobsState} className="min-h-0 flex-1" />
      )}
    </div>
  )
}
