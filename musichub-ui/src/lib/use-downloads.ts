import { getDownloads, type DownloadItem } from "@/lib/jobs-api"
import { useExecutionData } from "@/lib/use-execution-data"

export interface DownloadsState {
  downloads: DownloadItem[] | null
  error: string | null
}

// The candidates one Search for Missing Music execution downloaded (see `useExecutionData`).
export function useDownloads(executionId: string, version: string): DownloadsState {
  const { data, error } = useExecutionData(getDownloads, executionId, version)
  return { downloads: data, error }
}
