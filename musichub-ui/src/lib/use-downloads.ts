import { getDownloads, type DownloadItem } from "@/lib/jobs-api"
import { useExecutionData } from "@/lib/use-execution-data"

export interface DownloadsState {
  downloads: DownloadItem[] | null
  error: string | null
}

const noDownloads = async (): Promise<DownloadItem[]> => []

// The candidates one Search for Missing Music execution downloaded (see `useExecutionData`). A run of another task
// has none, so with `enabled` off nothing is requested.
export function useDownloads(
  executionId: string,
  version: string,
  enabled = true
): DownloadsState {
  const { data, error } = useExecutionData(
    enabled ? getDownloads : noDownloads,
    executionId,
    version
  )
  return { downloads: data, error }
}
