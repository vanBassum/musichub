import { FolderOpen, ListChecks, TriangleAlert } from "lucide-react"

// The management screens grouped under Settings, each with its own purpose.
export const settingsSections = [
  {
    id: "files",
    label: "Files",
    description: "Upload audio files and manage what is stored",
    icon: FolderOpen,
  },
  {
    id: "jobs",
    label: "Jobs",
    description: "Scheduled background tasks and their run history",
    icon: ListChecks,
  },
  {
    id: "problems",
    label: "Problems",
    description: "Files that could not be identified and failed tasks",
    icon: TriangleAlert,
  },
] as const

export type SettingsSectionId = (typeof settingsSections)[number]["id"]

export const defaultSettingsSection: SettingsSectionId = "files"

export function parseSettingsSection(
  value: string | undefined
): SettingsSectionId | null {
  return settingsSections.find((s) => s.id === value)?.id ?? null
}

export const formatSettingsHash = (section: SettingsSectionId) =>
  `#/settings/${section}`
